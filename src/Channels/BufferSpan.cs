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
        private readonly int _length;
        private readonly int _offset;

        internal BufferSpan(MemoryBlockSegment segment, int offset, int count)
        {
            _segment = segment;
            _length = count;
            _offset = offset;
        }

        public byte[] Array => _segment.Block.Array;

        public object UserData => _segment.Block.Slab.UserData;

        public IntPtr BufferPtr => _segment.Block.DataArrayPtr + _offset;

        public int Length => _length;

        public int Offset => _offset;

        public override string ToString()
        {
            return Encoding.UTF8.GetString(Array, Offset, Length);
        }

        public void CopyTo(ref WritableBuffer buffer)
        {
            buffer.Write(Array, Offset, Length);
        }
    }
}
