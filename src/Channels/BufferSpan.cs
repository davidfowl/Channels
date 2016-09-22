using System;

namespace Channels
{
    internal struct BufferSpan
    {
        private IBuffer _buffer;
        private int _start;
        private int _length;

        public BufferSpan(IBuffer buffer, int start, int length)
        {
            _buffer = buffer;
            _start = start;
            _length = length;
        }

        public Span<byte> Data => _buffer.Data.Slice(_start, _length);
    }
}
