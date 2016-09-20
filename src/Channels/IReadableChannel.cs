using System;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Defines a class that provides a channel from which data can be read.
    /// </summary>
    public interface IReadableChannel
    {
        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="IReadableChannel"/>.
        /// </summary>
        /// <returns>A <see cref="ChannelAwaitable"/> representing the asynchronous read operation.</returns>
        ChannelAwaitable ReadAsync();

        /// <summary>
        /// Gets a task that completes when no more data will be added to the channel.
        /// </summary>
        /// <remarks>This task indicates the producer has completed and will not write anymore data.</remarks>
        Task Reading { get; }

        /// <summary>
        /// Moves forward the channels read cursor to after the consumed data.
        /// </summary>
        /// <param name="consumed">Marks the extent of the data that has been succesfully proceesed.</param>
        /// <param name="examined">Marks the extent of the data that has been read and examined.</param>
        /// <remarks>
        /// The memory for the consumed data will be released and no longer available. 
        /// The examined data communicates to the channel when it should signal more data is available.
        /// </remarks>
        void Advance(ReadCursor consumed, ReadCursor examined);

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        void Complete(Exception exception = null);
    }
}
