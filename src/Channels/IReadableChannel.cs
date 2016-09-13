using System;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a channel from which data can be read.
    /// </summary>
    public interface IReadableChannel
    {
        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="IReadableChannel"/>.
        /// </summary>
        /// <returns>A <see cref="ChannelAwaitable"/> representing the asynchronous read operation.</returns>
        ChannelAwaitable ReadAsync();

        /// <summary>
        /// Gets a task that completes when the channel is completed and has no more data to be read.
        /// </summary>
        Task Completion { get; }

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        void CompleteReading(Exception exception = null);
    }
}
