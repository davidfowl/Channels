using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    // TODO: Replace this with Span<byte>
    public struct BufferSpan
    {
        public BufferSpan(IntPtr bufferPtr, byte[] data, int offset, int count)
        {
            BufferPtr = bufferPtr + offset;
            Buffer = new ArraySegment<byte>(data, offset, count);
        }

        public IntPtr BufferPtr { get; }

        public int Length => Buffer.Count;

        public ArraySegment<byte> Buffer { get; }
    }
}
