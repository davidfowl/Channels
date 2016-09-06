using System;

namespace Channels
{
    public struct MemoryBlockSpan
    {
        private MemoryPoolBlock _block;
        private int _start;
        private int _length;

        public MemoryBlockSpan(MemoryPoolBlock block, int start, int length)
        {
            _block = block;
            _start = start;
            _length = length;
        }

        public Span<byte> Data => _block == null ? Span<byte>.Empty : _block.Data.Slice(_start, _length);
    }
}
