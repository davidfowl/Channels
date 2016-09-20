// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Default <see cref="IWritableChannel"/> and <see cref="IReadableChannel"/> implementation.
    /// </summary>
    public class Channel : IReadableChannel, IWritableChannel
    {
        // TODO: Make this configurable for channel creation
        private const int _bufferSize = 4096;

        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private static Task _completedTask = Task.FromResult(0);

        private readonly IBufferPool _pool;

        private Action _awaitableState;

        private PooledBufferSegment _head;
        private PooledBufferSegment _tail;

        private int _consumingState;
        private int _producingState;
        private object _sync = new object();

        // REVIEW: This object might be getting a little big :)
        private readonly TaskCompletionSource<object> _readingTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _writingTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _startingReadingTcs = new TaskCompletionSource<object>();

        // the "tail" is the **end** of the data that we're writing - essentially the
        // write-head
        private PooledBufferSegment _writingTail;
        private int _writingTailIndex;

        // the "head" is the **start** of the data that we're writing; from the head,
        // you can iterate forward via .Next to access all of the data written
        // in this session
        private PooledBufferSegment _writingHead;
        private int _writingHeadIndex;

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
        public Task Reading => _readingTcs.Task;

        /// <summary>
        /// Gets a task that completes when no more data will be read from the channel.
        /// </summary>
        /// <remarks>
        /// This task indicates the consumer has completed and will not read anymore data.
        /// When this task is triggered, the producer should stop producing data.
        /// </remarks>
        public Task Writing => _writingTcs.Task;

        internal bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        internal Span<byte> Memory => _writingTail == null ? Span<byte>.Empty : _writingTail.Buffer.Data.Slice(_writingTail.End, _writingTail.Buffer.Data.Length - _writingTail.End);

        /// <summary>
        /// Allocates memory from the channel to write into.
        /// </summary>
        /// <param name="minimumSize">The minimum size buffer to allocate</param>
        /// <returns>A <see cref="WritableBuffer"/> that can be written to.</returns>
        public WritableBuffer Alloc(int minimumSize = 0)
        {
            if (Interlocked.CompareExchange(ref _producingState, 1, 0) != 0)
            {
                throw new InvalidOperationException("Already producing.");
            }

            PooledBufferSegment segment = null;

            if (_tail != null && !_tail.ReadOnly)
            {
                // Try to return the tail so the calling code can append to it
                int remaining = _tail.Buffer.Data.Length - _tail.End;

                if (minimumSize <= remaining)
                {
                    segment = _tail;
                }
            }

            if (segment == null && minimumSize > 0)
            {
                // We're out of tail space so lease a new segment only if the requested size > 0
                segment = new PooledBufferSegment(_pool.Lease(_bufferSize));
            }

            lock (_sync)
            {
                if (_head == null)
                {
                    _head = segment;
                }
                else if (segment != null && segment != _tail)
                {
                    // Append the segment to the tail if it's non-null
                    _tail.Next = segment;
                    _tail = segment;
                }

                _writingTail = segment;
                _writingTailIndex = segment?.End ?? 0;

                _writingHead = segment;
                _writingHeadIndex = _writingTailIndex;

                return new WritableBuffer(this);
            }
        }

        internal void Ensure(int count = 1)
        {
            EnsureAlloc();

            if (count > _bufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"Cannot allocate more than {_bufferSize} bytes in a single buffer");
            }

            if (_writingTail == null)
            {
                _writingTail = new PooledBufferSegment(_pool.Lease(_bufferSize));
                _writingTailIndex = _writingTail.End;
                _writingHead = _writingTail;
                _writingHeadIndex = _writingTail.Start;
            }

            Debug.Assert(_writingTail.Next == null);
            Debug.Assert(_writingTail.End == _writingTailIndex);

            var segment = _writingTail;
            var buffer = _writingTail.Buffer;
            var bufferIndex = _writingTailIndex;
            var bytesLeftInBuffer = buffer.Data.Length - bufferIndex;

            // If inadequate bytes left or if the segment is readonly
            if (bytesLeftInBuffer == 0 || bytesLeftInBuffer < count || segment.ReadOnly)
            {
                var nextBuffer = _pool.Lease(_bufferSize);
                var nextSegment = new PooledBufferSegment(nextBuffer);
                segment.End = bufferIndex;
                segment.Next = nextSegment;
                segment = nextSegment;
                buffer = nextBuffer;

                bufferIndex = 0;

                segment.End = bufferIndex;
                segment.Buffer = buffer;
                _writingTail = segment;
                _writingTailIndex = bufferIndex;
            }
        }

        internal void Append(ref ReadableBuffer buffer)
        {
            EnsureAlloc();

            PooledBufferSegment clonedEnd;
            var clonedBegin = PooledBufferSegment.Clone(buffer.Start, buffer.End, out clonedEnd);

            if (_writingTail == null)
            {
                _writingHead = clonedBegin;
                _writingHeadIndex = clonedBegin.Start;
            }
            else
            {
                Debug.Assert(_writingTail.Next == null);
                Debug.Assert(_writingTail.End == _writingTailIndex);

                _writingTail.Next = clonedBegin;
            }

            _writingTail = clonedEnd;
            _writingTailIndex = clonedEnd.End;
        }

        private void EnsureAlloc()
        {
            if (_producingState != 1)
            {
                throw new InvalidOperationException("No ongoing producing operation. Make sure Alloc() was called.");
            }
        }

        internal void Commit()
        {
            lock (_sync)
            {
                if (Interlocked.CompareExchange(ref _producingState, 0, 1) != 1)
                {
                    throw new InvalidOperationException("No ongoing producing operation to complete.");
                }

                if (_writingTail == null)
                {
                    // REVIEW: Should we signal the completion?
                    return;
                }

                if (_head == null)
                {
                    // Update the head to point to the head of the buffer. This
                    // happens if we called alloc(0) then write
                    _head = _writingHead;
                    _head.Start = _writingHeadIndex;
                }
                // If buffer.Head == tail it means we appended data to the tail
                else if (_tail != null && _writingHead != _tail)
                {
                    // If we have a tail point next to the head of the buffer
                    _tail.Next = _writingHead;
                }

                // Always update tail to the buffer's tail
                _tail = _writingTail;
                _tail.End = _writingTailIndex;
            }
        }

        internal void CommitBytes(int bytesWritten)
        {
            EnsureAlloc();

            if (bytesWritten > 0)
            {
                Debug.Assert(_writingTail != null);
                Debug.Assert(!_writingTail.ReadOnly);
                Debug.Assert(_writingTail.Next == null);
                Debug.Assert(_writingTail.End == _writingTailIndex);

                var buffer = _writingTail.Buffer;
                var bufferIndex = _writingTailIndex + bytesWritten;

                Debug.Assert(bufferIndex <= buffer.Data.Length);

                _writingTail.End = bufferIndex;
                _writingTailIndex = bufferIndex;
            }
            else if (bytesWritten < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesWritten));
            } // and if zero, just do nothing; don't need to validate tail etc
        }

        internal Task FlushAsync()
        {
            Commit();
            return CompleteWriteAsync();
        }

        internal ReadableBuffer AsReadableBuffer()
        {
            if (_writingHead == null)
            {
                return new ReadableBuffer(); // empty
            }

            return new ReadableBuffer(new ReadCursor(_writingHead, _writingHeadIndex), new ReadCursor(_writingTail, _writingTailIndex), isOwner: false);
        }

        private Task CompleteWriteAsync()
        {
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
            if (Interlocked.CompareExchange(ref _consumingState, 1, 0) != 0)
            {
                throw new InvalidOperationException("Already consuming.");
            }

            return new ReadableBuffer(new ReadCursor(_head), new ReadCursor(_tail, _tail?.End ?? 0));
        }

        void IReadableChannel.Advance(ReadCursor consumed, ReadCursor examined) => AdvanceReader(consumed, examined);

        public void AdvanceReader(ReadCursor consumed, ReadCursor examined)
        {
            PooledBufferSegment returnStart = null;
            PooledBufferSegment returnEnd = null;

            lock (_sync)
            {
                if (!consumed.IsDefault)
                {
                    returnStart = _head;
                    returnEnd = consumed.Segment;
                    _head = consumed.Segment;
                    _head.Start = consumed.Index;
                }

                if (!examined.IsDefault &&
                    examined.IsEnd &&
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

            if (Interlocked.CompareExchange(ref _consumingState, 0, 1) != 1)
            {
                throw new InvalidOperationException("No ongoing consuming operation to complete.");
            }
        }

        void IWritableChannel.Complete(Exception exception) => CompleteWriter(exception);

        /// <summary>
        /// Marks the channel as being complete, meaning no more items will be written to it.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void CompleteWriter(Exception exception = null)
        {
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

            Complete();
        }

        void IReadableChannel.Complete(Exception exception) => CompleteReader(exception);

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void CompleteReader(Exception exception = null)
        {
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
        /// <returns>A <see cref="ChannelAwaitable"/> representing the asynchronous read operation.</returns>
        public ChannelAwaitable ReadAsync() => new ChannelAwaitable(this);

        internal void OnCompleted(Action continuation)
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
                _readingTcs.SetException(new InvalidOperationException("Concurrent reads are not supported."));

                Interlocked.Exchange(
                    ref _awaitableState,
                    _awaitableIsCompleted);

                Task.Run(continuation);
                Task.Run(awaitableState);
            }
        }

        internal void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        internal ReadableBuffer GetResult()
        {
            if (!IsCompleted)
            {
                throw new InvalidOperationException("can't GetResult unless completed");
            }

            if (Reading.IsCompleted)
            {
                // Observe any exceptions if the reading task is completed
                Reading.GetAwaiter().GetResult();
            }

            return Read();
        }

        private void Dispose()
        {
            Debug.Assert(Writing.IsCompleted, "Not completed writing");
            Debug.Assert(Reading.IsCompleted, "Not completed reading");

            lock (_sync)
            {
                // Return all segments
                var segment = _head;
                while (segment != null)
                {
                    var returnSegment = segment;
                    segment = segment.Next;

                    returnSegment.Dispose();
                }

                _head = null;
                _tail = null;
            }
        }
    }
}
