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

        public UnownedBuffer(ArraySegment<byte> buffer) : base(buffer.Array, buffer.Offset, buffer.Count)
        {
            _buffer = buffer;
        }

        public IBuffer Preserve()
        {
            // Copy to a new Owned Buffer.
            var copy = new byte[_buffer.Count];
            Buffer.BlockCopy(_buffer.Array, _buffer.Offset, copy, 0, _buffer.Count);
            return new OwnedBuffer(copy);
        }
    }
}
