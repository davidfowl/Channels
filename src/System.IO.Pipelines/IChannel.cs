using System;

namespace Channels
{
    /// <summary>
    /// Defines a class that provides a duplex channel from which data can be read from and written to.
    /// </summary>
    public interface IChannel : IDisposable
    {
        /// <summary>
        /// Gets the <see cref="IReadableChannel"/> half of the duplex channel.
        /// </summary>
        IReadableChannel Input { get; }

        /// <summary>
        /// Gets the <see cref="IWritableChannel"/> half of the duplex channel.
        /// </summary>
        IWritableChannel Output { get; }
    }
}
