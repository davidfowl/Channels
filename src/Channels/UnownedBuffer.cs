using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that is owned by an external component.
    /// </summary>
    public class UnownedBuffer : IBuffer
    {
        private ArraySegment<byte> _buffer;

        public UnownedBuffer(ArraySegment<byte> buffer)
        {
            _buffer = buffer;
        }

        public Memory<byte> Data => new Memory<byte>(_buffer.Array, _buffer.Offset, _buffer.Count);

        public void Dispose()
        {
            // GC works
        }

        public IBuffer Preserve(int offset, int length)
        {
            // Copy to a new Owned Buffer.
            var buffer = new byte[length];
            Buffer.BlockCopy(_buffer.Array, _buffer.Offset + offset, buffer, 0, length);
            return new OwnedBuffer(buffer);
        }
    }
}
