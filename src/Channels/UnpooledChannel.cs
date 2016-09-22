using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public class UnpooledChannel : IReadableChannel, IReadableBufferAwaiter
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private static Task _completedTask = Task.FromResult(0);

        private Action _awaitableState;

        private BufferSegment _head;
        private BufferSegment _tail;

        private int _consumingState;
        private object _sync = new object();

        // REVIEW: This object might be getting a little big :)
        private readonly TaskCompletionSource<object> _readingTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _writingTcs = new TaskCompletionSource<object>();
        private readonly TaskCompletionSource<object> _startingReadingTcs = new TaskCompletionSource<object>();

        private Gate _readWaiting = new Gate();

        public UnpooledChannel()
        {
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

        bool IReadableBufferAwaiter.IsCompleted => IsCompleted;

        private bool IsCompleted => ReferenceEquals(_awaitableState, _awaitableIsCompleted);

        /// <summary>
        /// Writes a new buffer into the channel. The task returned by this operation only completes when the next
        /// Read has been queued, or the Reader has completed, since the buffer provided here needs to be kept alive
        /// until the matching Read finishes (because we don't have ownership tracking when working with unowned buffers)
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public Task WriteAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return WriteAsync(new UnpooledBuffer(buffer), cancellationToken);
        }

        /// <summary>
        /// Writes a new buffer into the channel. The task returned by this operation only completes when the next
        /// Read has been queued, or the Reader has completed, since the buffer provided here needs to be kept alive
        /// until the matching Read finishes (because we don't have ownership tracking when working with unowned buffers)
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public async Task WriteAsync(IBuffer buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (cancellationToken.Register(state => ((UnpooledChannel)state).Cancel(), this))
            {
                lock (_sync)
                {
                    var segment = new BufferSegment(buffer);
                    segment.End = buffer.Data.Length;

                    if (_head == null)
                    {
                        // Update the head to point to the head of the buffer. This
                        // happens if we called alloc(0) then write
                        _head = segment;
                    }
                    else if (_tail != null)
                    {
                        _tail.Next = segment;
                    }

                    // Always update tail to the buffer's tail
                    _tail = segment;

                    Complete();
                }

                await _readWaiting;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private void Cancel()
        {
            try
            {
                // Is this stupid? Should we just pass an exception in? I'm trying to get a useful call stack...
                throw new OperationCanceledException();
            }
            catch (OperationCanceledException ex)
            {
                // Allow the WriteAsync end to throw the OperationCanceledException
                _readWaiting.Open();

                // Complete the Writer end with OperationCanceledException
                CompleteWriter(ex);
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

        private void AdvanceReader(ReadCursor consumed, ReadCursor examined)
        {
            BufferSegment returnStart = null;
            BufferSegment returnEnd = null;

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
        private void CompleteReader(Exception exception = null)
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

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="IReadableChannel"/>.
        /// </summary>
        /// <returns>A <see cref="ReadableBufferAwaitable"/> representing the asynchronous read operation.</returns>
        public ReadableBufferAwaitable ReadAsync() => new ReadableBufferAwaitable(this);

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
                _readingTcs.SetException(new InvalidOperationException("Concurrent reads are not supported."));

                Interlocked.Exchange(
                    ref _awaitableState,
                    _awaitableIsCompleted);

                Task.Run(continuation);
                Task.Run(awaitableState);
            }
        }

        ReadableBuffer IReadableBufferAwaiter.GetBuffer()
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
