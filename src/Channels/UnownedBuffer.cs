using System;
using System.Buffers;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that is owned by an external component.
    /// </summary>
    public class UnownedBuffer : OwnedMemory<byte>, IBuffer
    {
        private ArraySegment<byte> _buffer;

        public UnownedBuffer(ArraySegment<byte> buffer)
        {
            _buffer = buffer;
        }

        public Memory<byte> Data => Memory;

        public IBuffer Preserve()
        {
            // Copy to a new Owned Buffer.
            var copy = new byte[_buffer.Count];
            Buffer.BlockCopy(_buffer.Array, _buffer.Offset, copy, 0, _buffer.Count);
            return new OwnedBuffer(copy);
        }

        protected override void DisposeCore()
        {
            // GC works
        }

        protected override Span<byte> GetSpanCore()
        {
            return _buffer;
        }

        protected override bool TryGetArrayCore(out ArraySegment<byte> buffer)
        {
            buffer = _buffer;
            return true;
        }

        protected override unsafe bool TryGetPointerCore(out void* pointer)
        {
            pointer = null;
            return false;
        }
    }
}
