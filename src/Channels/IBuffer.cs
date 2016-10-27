using System;

namespace Channels
{
    /// <summary>
    /// An interface that represents a span of memory
    /// </summary>
    public interface IBuffer : IDisposable
    {
        /// <summary>
        /// Preserves the a portion of the data this buffer represents
        /// </summary>
        IBuffer Preserve();

        /// <summary>
        /// Raw representation of the underlying data this <see cref="IBuffer"/> represents
        /// </summary>
        Memory<byte> Memory { get; }
    }
}
