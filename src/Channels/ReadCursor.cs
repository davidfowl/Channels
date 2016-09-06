using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Channels
{
    public struct ReadCursor : IEquatable<ReadCursor>
    {
        private MemoryBlockSegment _segment;
        private int _index;

        internal ReadCursor(MemoryBlockSegment segment)
        {
            _segment = segment;
            _index = segment?.Start ?? 0;
        }

        internal ReadCursor(MemoryBlockSegment segment, int index)
        {
            _segment = segment;
            _index = index;
        }

        internal MemoryBlockSegment Segment => _segment;

        internal int Index => _index;

        internal bool IsDefault => _segment == null;

        public bool IsEnd
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var segment = _segment;

                if (segment == null)
                {
                    return true;
                }
                else if (_index < segment.End)
                {
                    return false;
                }
                else if (segment.Next == null)
                {
                    return true;
                }
                else
                {
                    return IsEndMultiBlock();
                }
            }
        }

        private bool IsEndMultiBlock()
        {
            var segment = _segment.Next;
            while (segment != null)
            {
                if (segment.Start < segment.End)
                {
                    return false; // subsequent block has data - IsEnd is false
                }
                segment = segment.Next;
            }
            return true;
        }

        internal int GetLength(ReadCursor end)
        {
            if (IsDefault)
            {
                return 0;
            }

            var segment = _segment;
            var index = _index;
            var length = 0;
            checked
            {
                while (true)
                {
                    if (segment == end._segment)
                    {
                        return length + end._index - index;
                    }
                    else if (segment.Next == null)
                    {
                        return length;
                    }
                    else
                    {
                        length += segment.End - index;
                        segment = segment.Next;
                        index = segment.Start;
                    }
                }
            }
        }

        internal int Seek(int bytes)
        {
            if (IsEnd)
            {
                return 0;
            }

            var wasLastBlock = _segment.Next == null;
            var following = _segment.End - _index;

            if (following >= bytes)
            {
                _index += bytes;
                return bytes;
            }

            var segment = _segment;
            var index = _index;
            while (true)
            {
                if (wasLastBlock)
                {
                    _segment = segment;
                    _index = index + following;
                    return following;
                }
                else
                {
                    bytes -= following;
                    segment = segment.Next;
                    index = segment.Start;
                }

                wasLastBlock = segment.Next == null;
                following = segment.End - index;

                if (following >= bytes)
                {
                    _segment = segment;
                    _index = index + bytes;
                    return bytes;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetBuffer(ReadCursor end, out MemoryBlockSpan span)
        {
            if (IsDefault)
            {
                span = default(MemoryBlockSpan);
                return false;
            }

            var segment = _segment;
            var index = _index;

            if (end.Segment == segment)
            {
                var following = end.Index - index;

                if (following > 0)
                {
                    span = new MemoryBlockSpan(segment.Block, index, following);

                    _index = index + following;
                    return true;
                }

                span = default(MemoryBlockSpan);
                return false;
            }
            else
            {
                return TryGetBufferMultiBlock(end, out span);
            }
        }

        private bool TryGetBufferMultiBlock(ReadCursor end, out MemoryBlockSpan span)
        {
            var segment = _segment;
            var index = _index;

            // Determine if we might attempt to copy data from segment.Next before
            // calculating "following" so we don't risk skipping data that could
            // be added after segment.End when we decide to copy from segment.Next.
            // segment.End will always be advanced before segment.Next is set.

            int following = 0;

            while (true)
            {
                var wasLastBlock = segment.Next == null || end.Segment == segment;

                if (end.Segment == segment)
                {
                    following = end.Index - index;
                }
                else
                {
                    following = segment.End - index;
                }

                if (following > 0)
                {
                    break;
                }

                if (wasLastBlock)
                {
                    span = default(MemoryBlockSpan);
                    return false;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }
            }

            span = new MemoryBlockSpan(segment.Block, index, following);

            _segment = segment;
            _index = index + following;
            return true;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var span = Segment.Block.Data.Slice(Index, Segment.End - Index);
            for (int i = 0; i < span.Length; i++)
            {
                sb.Append((char)span[i]);
            }
            return sb.ToString();
        }

        public bool Equals(ReadCursor other)
        {
            return other._segment == _segment && other._index == _index;
        }

        public override bool Equals(object obj)
        {
            return Equals((ReadCursor)obj);
        }

        public override int GetHashCode()
        {
            var h1 = _segment?.GetHashCode() ?? 0;
            var h2 = _index.GetHashCode();

            var shift5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)shift5 + h1) ^ h2;
        }
    }
}
