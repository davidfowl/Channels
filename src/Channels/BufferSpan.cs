using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Channels
{
    // TODO: Replace this with Span<byte>
    public struct BufferSpan
    {
        private readonly MemoryBlockSegment _segment;

        internal BufferSpan(MemoryBlockSegment segment, int offset, int count)
        {
            _segment = segment;
            BufferPtr = segment.Block.DataArrayPtr + offset;
            Buffer = new ArraySegment<byte>(segment.Block.Array, offset, count);
        }

        public object UserData => _segment.Block.Slab.UserData;

        public IntPtr BufferPtr { get; }

        public int Length => Buffer.Count;

        public ArraySegment<byte> Buffer { get; }

        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(Buffer.Array[i + Buffer.Offset].ToString("X2"));
            }
            return builder.ToString();
        }

        public void CopyTo(ref WritableBuffer buffer)
        {
            buffer.Write(Buffer.Array, Buffer.Offset, Buffer.Count);
        }
    }
}
