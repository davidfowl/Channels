using System;
using System.Buffers;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that is completely owned by this object.
    /// </summary>
    public class OwnedBuffer : IBuffer
    {
        private byte[] _buffer;

        /// <summary>
        /// Create a new instance of <see cref="OwnedBuffer"/> that spans the array provided.
        /// </summary>
        public OwnedBuffer(byte[] buffer)
        {
            _buffer = buffer;
        }

        /// <summary>
        /// Raw representation of the underlying data this <see cref="IBuffer"/> represents
        /// </summary>
        public Memory<byte> Data => new Memory<byte>(_buffer);

        void IDisposable.Dispose()
        {
            // No need, the GC can handle it.
        }

        // We're owned, we're always "preserved"
        /// <summary>
        /// <see cref="IBuffer.Preserve(int, int, out int, out int)"/>
        /// </summary>
        public IBuffer Preserve(int offset, int length, out int newStart, out int newEnd)
        {
            newStart = offset;
            newEnd = offset + length;
            return this;
        }
    }
}