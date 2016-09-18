using System;
using System.Threading.Tasks;

namespace Channels
{
    public abstract class ReadableChannel : IReadableChannel
    {
        protected Channel _channel;

        public ReadableChannel(IBufferPool pool)
        {
            _channel = new Channel(pool);
        }

        /// <summary>
        /// Gets a task that completes when no more data will be added to the channel.
        /// </summary>
        /// <remarks>This task indicates the producer has completed and will not write anymore data.</remarks>
        public Task Reading => _channel.Reading;

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
