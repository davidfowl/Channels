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
        public BufferSpan(IntPtr bufferPtr, byte[] data, int offset, int count)
        {
            BufferPtr = bufferPtr + offset;
            Buffer = new ArraySegment<byte>(data, offset, count);
        }

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
