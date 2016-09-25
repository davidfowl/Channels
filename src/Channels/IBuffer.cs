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
        /// <param name="offset">The offset to preserve</param>
        /// <param name="length">The length of the buffer to preserve</param>
        IBuffer Preserve(int offset, int length);

        /// <summary>
        /// Raw representation of the underlying data this <see cref="IBuffer"/> represents
        /// </summary>
        Memory<byte> Data { get; }
    }
}
