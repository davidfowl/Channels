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
        /// Creates a base <see cref="ReadableChannel"/>.
        /// </summary>
        /// <param name="channel">The <see cref="Channel"/> the ReadableChannel communicates over.</param>
        protected ReadableChannel(Channel channel)
        {
            _channel = channel;
        }

        /// <summary>
        /// Moves forward the channels read cursor to after the consumed data.
        /// </summary>
        /// <param name="consumed">Marks the extent of the data that has been succesfully proceesed.</param>
        /// <param name="examined">Marks the extent of the data that has been read and examined.</param>
        /// <remarks>
        /// The memory for the consumed data will be released and no longer available.
        /// The examined data communicates to the channel when it should signal more data is available.
        /// </remarks>
        public void Advance(ReadCursor consumed, ReadCursor examined) => _channel.AdvanceReader(consumed, examined);

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        public void Complete(Exception exception = null) => _channel.CompleteReader(exception);

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="ReadableChannel"/>.
        /// </summary>
        /// <returns>A <see cref="ReadableChannelAwaitable"/> representing the asynchronous read operation.</returns>
        public ReadableChannelAwaitable ReadAsync() => _channel.ReadAsync();
    }
}
