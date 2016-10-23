// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Default <see cref="IWritableChannel"/> and <see cref="IReadableChannel"/> implementation.
    /// </summary>
    public class Channel : IReadableChannel, IWritableChannel, IReadableBufferAwaiter
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private static Task _completedTask = Task.FromResult(0);

        private readonly IBufferPool _pool;

        private Action _awaitableState;

        // The read head which is the extent of the IReadableChannel's consumed bytes
        private ReadCursor _readHead;

        // The commit head which is the extent of the bytes available to the IReadableChannel to consume
        private ReadCursor _commitHead;

        // The write head which is the extent of the IWritableChannel's written bytes
        private ReadCursor _writingHead;

        private int _consumingState;
        private int _producingState;
        private object _sync = new object();

        // REVIEW: This object might be getting a little big :)
        private readonly TaskCompletionSource<object> _readingTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _writingTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _startingReadingTcs = new TaskCompletionSource<object>();
#if DEBUG
        private string _consumingLocation;
#endif
        /// <summary>
        /// Initializes the <see cref="Channel"/> with the specifed <see cref="IBufferPool"/>.
        /// </summary>
        /// <param name="pool"></param>
        public Channel(IBufferPool pool)
        {
            _pool = pool;
            _awaitableState = _awaitableIsNotCompleted;
        }

        /// <summary>
        /// A <see cref="Task"/> that completes when the consumer starts consuming the <see cref="IReadableChannel"/>.
        /// </summary>
        public Task ReadingStarted => _startingReadingTcs.Task;

        /// <summary>
        /// Gets a task that completes when no more data will be added to the channel.
        /// </summary>
        /// <remarks>This task indicates the producer has completed and will not write anymore data.</remarks>
        private Task Reading => _readingTcs.Task;

        /// <summary>
        /// Gets a task that completes when no more data will be read from the channel.
        /// </summary>
        /// <remarks>
        /// This task indicates the consumer has completed and will not read anymore data.
        /// When this task is triggered, the producer should stop producing data.
        /// </remarks>
        public Task Writing => _writingTcs.Task;

        bool IReadableBufferAwaiter.IsCompleted => IsCompleted;

        private bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        internal Memory<byte> Memory => _writingHead.Segment == null ? Memory<byte>.Empty : _writingHead.Segment.Data.Slice(_writingHead.Index, _writingHead.Segment.Length - _writingHead.Index);

        /// <summary>
        /// Allocates memory from the channel to write into.
        /// </summary>
        /// <param name="minimumSize">The minimum size buffer to allocate</param>
        /// <returns>A <see cref="WritableBuffer"/> that can be written to.</returns>
        public WritableBuffer Alloc(int minimumSize = 0)
        {
            // CompareExchange not required as its setting to current value if test fails
            if (Interlocked.Exchange(ref _producingState, State.Active) != State.NotActive)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.AlreadyProducing);
            }

            if (minimumSize < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.minimumSize);
            }
            else if (minimumSize > 0)
            {
                AllocateWriteHead(minimumSize);
            }

            return new WritableBuffer(this);
        }

        internal void Ensure(int count = 1)
        {
            EnsureAlloc();

            var segment = _writingHead.Segment;
            if (segment == null)
            {
                segment = AllocateWriteHead(count);
            }

            var bytesLeftInBuffer = segment.Length - _writingHead.Index;

            // If inadequate bytes left or if the segment is readonly
            if (bytesLeftInBuffer == 0 || bytesLeftInBuffer < count || segment.ReadOnly)
            {
                // Trim the previous segment since we're not writing to it anymore
                segment.Trim(bytesLeftInBuffer);

                var nextBuffer = _pool.Lease(count);
                var nextSegment = new BufferSegment(nextBuffer);

                segment.Next = nextSegment;

                _writingHead = new ReadCursor(nextSegment);
            }
        }

        private BufferSegment AllocateWriteHead(int count)
        {
            BufferSegment segment = null;

            if (_commitHead.Segment != null && !_commitHead.Segment.ReadOnly)
            {
                // Try to return the tail so the calling code can append to it
                int remaining = _commitHead.Segment.Length - _commitHead.Index;

                if (count <= remaining)
                {
                    // Free tail space of the right amount, use that
                    segment = _commitHead.Segment;
                }
                else
                {
                    _commitHead.Segment.Trim(remaining);
                }
            }

            if (segment == null)
            {
                // No free tail space, allocate a new segment
                segment = new BufferSegment(_pool.Lease(count));
            }

            // Changing commit head shared with Reader
            lock (_sync)
            {
                if (_commitHead.Segment == null)
                {
                    // No previous writes have occurred
                    _commitHead = new ReadCursor(segment);
                }
                else if (segment != _commitHead.Segment && _commitHead.Segment.Next == null)
                {
                    // Append the segment to the commit head if writes have been committed
                    // and it isn't the same segment (unused tail space)
                    _commitHead.Segment.Next = segment;
                }
            }

            // Set write head to assigned segment
            _writingHead = _commitHead;

            return segment;
        }

        internal void Append(ReadableBuffer buffer)
        {
            if (buffer.IsEmpty)
            {
                return; // nothing to do
            }

            EnsureAlloc();

            BufferSegment clonedEnd;
            var clonedBegin = BufferSegment.Clone(buffer.Start, buffer.End, out clonedEnd);

            if (_writingHead.Segment == null)
            {
                // No active write

                if (_commitHead.Segment == null)
                {
                    // No allocated buffers yet, not locking as _readHead will be null
                    _commitHead = new ReadCursor(clonedBegin);
                }
                else
                {
                    Debug.Assert(_commitHead.Segment.Next == null);
                    // Allocated buffer, append as next segment
                    _commitHead.Segment.Next = clonedBegin;
                }
            }
            else
            {
                Debug.Assert(_writingHead.Segment.Next == null);
                // Active write, append as next segment
                _writingHead.Segment.Next = clonedBegin;
            }

            // Move write head to end of buffer
            _writingHead = new ReadCursor(clonedEnd, clonedEnd.Length);
        }

        private void EnsureAlloc()
        {
            if (_producingState == State.NotActive)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.NotProducingNoAlloc);
            }
        }

        internal void Commit()
        {
            // CompareExchange not required as its setting to current value if test fails
            if (Interlocked.Exchange(ref _producingState, State.NotActive) != State.Active)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.NotProducingToComplete);
            }

            if (_writingHead.Segment == null)
            {
                // Nothing written to commit
                return;
            }

            // Changing commit head shared with Reader
            lock (_sync)
            {
                if (_readHead.Segment == null)
                {
                    // Update the head to point to the head of the buffer. 
                    // This happens if we called alloc(0) then write
                    _readHead = _commitHead;
                }

                // Always move the commit head to the write head
                _commitHead = _writingHead;
            }

            // Clear the writing state
            _writingHead = default(ReadCursor);
        }

        public void AdvanceWriter(int bytesWritten)
        {
            EnsureAlloc();

            if (bytesWritten > 0)
            {
                Debug.Assert(_writingHead.Segment != null);
                Debug.Assert(!_writingHead.Segment.ReadOnly);
                Debug.Assert(_writingHead.Segment.Next == null);

                var buffer = _writingHead.Segment.Data;
                var bufferIndex = _writingHead.Index + bytesWritten;

                Debug.Assert(bufferIndex <= buffer.Length);

                _writingHead = new ReadCursor(_writingHead.Segment, bufferIndex);
            }
            else if (bytesWritten < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.bytesWritten);
            } // and if zero, just do nothing; don't need to validate tail etc
        }

        internal Task FlushAsync()
        {
            if (_producingState == State.Active)
            {
                // Commit the data as not already committed
                Commit();
            }

            return CompleteWriteAsync();
        }

        internal ReadableBuffer AsReadableBuffer()
        {
            if (_writingHead.Segment == null)
            {
                return new ReadableBuffer(); // Nothing written return empty
            }

            return new ReadableBuffer(_commitHead, _writingHead);
        }

        private Task CompleteWriteAsync()
        {
            // TODO: Can factor out this lock
            lock (_sync)
            {
                Complete();

                // Apply back pressure here
                return _completedTask;
            }
        }

        private void Complete()
        {
            var awaitableState = Interlocked.Exchange(
                ref _awaitableState,
                _awaitableIsCompleted);

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                awaitableState();
            }
        }

        private ReadableBuffer Read()
        {
            // CompareExchange not required as its setting to current value if test fails
            if (Interlocked.Exchange(ref _consumingState, State.Active) != State.NotActive)
            {
#if DEBUG
                var message = "Already consuming.";
                message += " From: " + _consumingLocation;
                throw new InvalidOperationException(message);
#else
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.AlreadyConsuming);
#endif
            }
#if DEBUG
            _consumingLocation = Environment.StackTrace;
#endif
            ReadCursor readEnd;
            // Reading commit head shared with writer
            lock (_sync)
            {
                readEnd = _commitHead;
            }

            return new ReadableBuffer(_readHead, readEnd);
        }

        void IReadableChannel.Advance(ReadCursor consumed, ReadCursor examined) => AdvanceReader(consumed, examined);

        public void AdvanceReader(ReadCursor consumed, ReadCursor examined)
        {
            BufferSegment returnStart = null;
            BufferSegment returnEnd = null;

            if (!consumed.IsDefault)
            {
                returnStart = _readHead.Segment;
                returnEnd = consumed.Segment;
                _readHead = consumed;
            }

            // Reading commit head shared with writer
            lock (_sync)
            {
                if (!examined.IsDefault &&
                    examined.Segment == _commitHead.Segment &&
                    examined.Index == _commitHead.Index &&
                    Reading.Status == TaskStatus.WaitingForActivation)
                {
                    Interlocked.CompareExchange(
                        ref _awaitableState,
                        _awaitableIsNotCompleted,
                        _awaitableIsCompleted);
                }
            }

            while (returnStart != returnEnd)
            {
                var returnSegment = returnStart;
                returnStart = returnStart.Next;
                returnSegment.Dispose();
            }

#if DEBUG
            _consumingLocation = null;
#endif
            // CompareExchange not required as its setting to current value if test fails
            if (Interlocked.Exchange(ref _consumingState, State.NotActive) != State.Active)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.NotConsumingToComplete);
            }
        }

        void IWritableChannel.Complete(Exception exception) => CompleteWriter(exception);

        /// <summary>
        /// Marks the channel as being complete, meaning no more items will be written to it.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void CompleteWriter(Exception exception = null)
        {
            // TODO: Review this lock?
            lock (_sync)
            {
                SignalReader(exception);

                if (Writing.IsCompleted)
                {
                    Dispose();
                }
            }
        }

        private void SignalReader(Exception exception)
        {
            if (exception != null)
            {
                _readingTcs.TrySetException(exception);
            }
            else
            {
                _readingTcs.TrySetResult(null);
            }

            FlushAsync();
        }

        void IReadableChannel.Complete(Exception exception) => CompleteReader(exception);

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void CompleteReader(Exception exception = null)
        {
            // TODO: Review this lock?
            lock (_sync)
            {
                SignalWriter(exception);

                if (Reading.IsCompleted)
                {
                    Dispose();
                }
            }
        }

        private void SignalWriter(Exception exception)
        {
            if (exception != null)
            {
                _writingTcs.TrySetException(exception);
            }
            else
            {
                _writingTcs.TrySetResult(null);
            }
        }

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="IReadableChannel"/>.
        /// </summary>
        /// <returns>A <see cref="ReadableChannelAwaitable"/> representing the asynchronous read operation.</returns>
        public ReadableChannelAwaitable ReadAsync() => new ReadableChannelAwaitable(this);

        void IReadableBufferAwaiter.OnCompleted(Action continuation)
        {
            _startingReadingTcs.TrySetResult(null);

            var awaitableState = Interlocked.CompareExchange(
                ref _awaitableState,
                continuation,
                _awaitableIsNotCompleted);

            if (ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                return;
            }
            else if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                // Dispatch here to avoid stack diving
                // Task.Run(continuation);
                continuation();
            }
            else
            {
                _readingTcs.SetException(ThrowHelper.GetInvalidOperationException(ExceptionResource.NoConcurrentReads));

                Interlocked.Exchange(
                    ref _awaitableState,
                    _awaitableIsCompleted);

                Task.Run(continuation);
                Task.Run(awaitableState);
            }
        }

        ChannelReadResult IReadableBufferAwaiter.GetResult()
        {
            if (!IsCompleted)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.GetResultNotCompleted);
            }

            var readingIsCompleted = Reading.IsCompleted;
            if (readingIsCompleted)
            {
                // Observe any exceptions if the reading task is completed
                Reading.GetAwaiter().GetResult();
            }

            return new ChannelReadResult(Read(), readingIsCompleted);
        }

        private void Dispose()
        {
            Debug.Assert(Writing.IsCompleted, "Not completed writing");
            Debug.Assert(Reading.IsCompleted, "Not completed reading");

            // TODO: Review throw if not completed?

            lock (_sync)
            {
                // Return all segments
                var segment = _readHead.Segment;
                while (segment != null)
                {
                    var returnSegment = segment;
                    segment = segment.Next;

                    returnSegment.Dispose();
                }

                _readHead = default(ReadCursor);
                _commitHead = default(ReadCursor);
            }
        }

        // Can't use enums with Interlocked
        private static class State
        {
            public static int NotActive = 0;
            public static int Active = 1;
        }
    }
}
