using System;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a channel from which data can be read.
    /// </summary>
    public abstract class ReadableChannel : IReadableChannel
    {
        /// <summary>
        /// The underlying <see cref="Channel"/> the ReadableChannel communicates over.
        /// </summary>
        protected readonly Channel _channel;

        /// <summary>
        /// Creates a base <see cref="ReadableChannel"/>.
        /// </summary>
        /// <param name="pool">The <see cref="IBufferPool"/> that buffers will be allocated from.</param>
        protected ReadableChannel(IBufferPool pool)
        {
            _channel = new Channel(pool);
        }

        /// <summary>
        /// Gets a task that completes when no more data will be added to the channel.
        /// </summary>
        /// <remarks>This task indicates the producer has completed and will not write anymore data.</remarks>
        public Task Reading => _channel.Reading;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="consumed"></param>
        /// <param name="examined"></param>
        public void Advance(ReadCursor consumed, ReadCursor examined) => _channel.AdvanceReader(consumed, examined);

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void Complete(Exception exception = null) => _channel.CompleteReader(exception);

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="ReadableChannel"/>.
        /// </summary>
        /// <returns>A <see cref="ChannelAwaitable"/> representing the asynchronous read operation.</returns>
        public ChannelAwaitable ReadAsync() => _channel.ReadAsync();
    }
}
