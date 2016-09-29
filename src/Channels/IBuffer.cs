using System;
using System.Buffers;

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
        /// <param name="newStart">The new starting position of the buffer</param>
        /// <param name="newEnd">The new ending position of the buffer</param>
        IBuffer Preserve(int offset, int length, out int newStart, out int newEnd);

        /// <summary>
        /// Raw representation of the underlying data this <see cref="IBuffer"/> represents
        /// </summary>
        Memory<byte> Data { get; }
    }
}
