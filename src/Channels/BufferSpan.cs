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
        internal BufferSpan(MemoryBlockSegment segment, int offset, int count)
        {
            UserData = segment.Block.Slab.UserData;
            BufferPtr = segment.Block.DataArrayPtr + offset;
            Length = count;
            Offset = offset;
            Array = segment.Block.Array;
        }

        public byte[] Array { get; }

        public object UserData { get; }

        public IntPtr BufferPtr { get; }

        public int Length { get; }
        public int Offset { get; }

        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(Array[i + Offset].ToString("X2"));
            }
            return builder.ToString();
        }

        public void CopyTo(ref WritableBuffer buffer)
        {
            buffer.Write(Array, Offset, Length);
        }
    }
}
