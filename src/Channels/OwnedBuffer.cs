using System;
using System.Buffers;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that is completely owned by this object.
    /// </summary>
    public class OwnedBuffer : OwnedMemory<byte>, IBuffer
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
        public Memory<byte> Data => Memory;

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

        protected override Span<byte> GetSpanCore()
        {
            return _buffer;
        }

        protected override void DisposeCore()
        {
            // No need, the GC can handle it.
        }

        protected override bool TryGetArrayCore(out ArraySegment<byte> buffer)
        {
            buffer = new ArraySegment<byte>(_buffer);
            return true;
        }

        protected override unsafe bool TryGetPointerCore(out void* pointer)
        {
            pointer = null;
            return false;
        }
    }
}