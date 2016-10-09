using System;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a channel to which data can be written.
    /// </summary>
    public abstract class WritableChannel : IWritableChannel
    {
        /// <summary>
        /// The underlying <see cref="Channel"/> the WritableChannel communicates over.
        /// </summary>
        protected readonly Channel _channel;

        internal WritableChannel(Channel channel)
        {
            _channel = (Channel)this;
        }

        /// <summary>
        /// Creates a base <see cref="WritableChannel"/>.
        /// </summary>
        /// <param name="pool">The <see cref="IBufferPool"/> that buffers will be allocated from.</param>
        protected WritableChannel(IBufferPool pool)
        {
            _channel = new Channel(pool);
        }

        internal Channel Channel => _channel;

        /// <summary>
        /// Gets a task that completes when no more data will be read from the channel.
        /// </summary>
        /// <remarks>
        /// This task indicates the consumer has completed and will not read anymore data.
        /// When this task is triggered, the producer should stop producing data.
        /// </remarks>
        public Task Writing => _channel.Writing;

        /// <summary>
        /// Allocates memory from the channel to write into.
        /// </summary>
        /// <param name="minimumSize">The minimum size buffer to allocate</param>
        /// <returns>A <see cref="WritableBuffer"/> that can be written to.</returns>
        public WritableBuffer Alloc(int minimumSize = 0) => _channel.Alloc(minimumSize);

        /// <summary>
        /// Marks the channel as being complete, meaning no more items will be written to it.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void Complete(Exception exception = null) => _channel.CompleteWriter(exception);

        /// <summary>
        /// Commits all outstanding written data to the underlying <see cref="IWritableChannel"/> so they can be read
        /// and seals the <see cref="WritableBuffer"/> so no more data can be committed.
        /// </summary>
        /// <remarks>
        /// While an on-going conncurent read may pick up the data, <see cref="FlushAsync"/> should be called to signal the reader.
        /// </remarks>
        public virtual void Commit()
        {
            _channel.Commit();
        }

        /// <summary>
        /// Signals the <see cref="IReadableChannel"/> data is available.
        /// Will <see cref="Commit"/> if necessary.
        /// </summary>
        /// <returns>A task that completes when the data is fully flushed.</returns>
        public virtual Task FlushAsync()
        {
            return _channel.FlushAsync();
        }
    }
}
