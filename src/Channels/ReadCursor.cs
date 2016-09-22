using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Channels
{
    /// <summary>
    /// Represents a fixed point in a readable buffer
    /// </summary>
    public struct ReadCursor : IEquatable<ReadCursor>
    {
        /// <summary>
        /// Result when a search operation cannot find the requested value
        /// </summary>
        public static ReadCursor NotFound => default(ReadCursor);

        private BufferSegment _segment;
        private int _index;

        internal ReadCursor(BufferSegment segment)
        {
            _segment = segment;
            _index = segment?.Start ?? 0;
        }

        internal ReadCursor(BufferSegment segment, int index)
        {
            _segment = segment;
            _index = index;
        }

        internal BufferSegment Segment => _segment;

        internal int Index => _index;

        internal bool IsDefault => _segment == null;

        internal bool IsEnd
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
                    return IsEndMultiSegment();
                }
            }
        }

        private bool IsEndMultiSegment()
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

        internal ReadCursor Seek(int bytes)
        {
            int count;
            return Seek(bytes, out count);
        }

        internal ReadCursor Seek(int bytes, out int bytesSeeked)
        {
            if (IsEnd)
            {
                bytesSeeked = 0;
                return this;
            }

            var wasLastSegment = _segment.Next == null;
            var following = _segment.End - _index;

            if (following >= bytes)
            {
                bytesSeeked = bytes;
                return new ReadCursor(Segment, _index + bytes);
            }

            var segment = _segment;
            var index = _index;
            while (true)
            {
                if (wasLastSegment)
                {
                    bytesSeeked = following;
                    return new ReadCursor(segment, index + following);
                }
                else
                {
                    bytes -= following;
                    segment = segment.Next;
                    index = segment.Start;
                }

                wasLastSegment = segment.Next == null;
                following = segment.End - index;

                if (following >= bytes)
                {
                    bytesSeeked = bytes;
                    return new ReadCursor(segment, index + bytes);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetBuffer(ReadCursor end, out BufferSpan span, out ReadCursor cursor)
        {
            if (IsDefault)
            {
                span = default(BufferSpan);
                cursor = this;
                return false;
            }

            var segment = _segment;
            var index = _index;

            if (end.Segment == segment)
            {
                var following = end.Index - index;

                if (following > 0)
                {
                    span = new BufferSpan(segment.Buffer, index, following);
                    cursor = new ReadCursor(segment, index + following);
                    return true;
                }

                span = default(BufferSpan);
                cursor = this;
                return false;
            }
            else
            {
                return TryGetBufferMultiBlock(end, out span, out cursor);
            }
        }

        private bool TryGetBufferMultiBlock(ReadCursor end, out BufferSpan span, out ReadCursor cursor)
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
                var wasLastSegment = segment.Next == null || end.Segment == segment;

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

                if (wasLastSegment)
                {
                    span = default(BufferSpan);
                    cursor = this;
                    return false;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }
            }

            span = new BufferSpan(segment.Buffer, index, following);
            cursor = new ReadCursor(segment, index + following);
            return true;
        }

        /// <summary>
        /// See <see cref="object.ToString"/>
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            var span = Segment.Buffer.Data.Slice(Index, Segment.End - Index);
            for (int i = 0; i < span.Length; i++)
            {
                sb.Append((char)span[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Compares two cursors for equality
        /// </summary>
        public static bool operator ==(ReadCursor c1, ReadCursor c2)
        {
            return c1.Equals(c2);
        }

        /// <summary>
        /// Compares two cursors for inequality
        /// </summary>

        public static bool operator !=(ReadCursor c1, ReadCursor c2)
        {
            return !c1.Equals(c2);
        }

        /// <summary>
        /// Compares two cursors for equality
        /// </summary>
        public bool Equals(ReadCursor other)
        {
            return other._segment == _segment && other._index == _index;
        }

        /// <summary>
        /// Compares two cursors for equality
        /// </summary>
        public override bool Equals(object obj)
        {
            return Equals((ReadCursor)obj);
        }

        /// <summary>
        /// See <see cref="object.GetHashCode"/>
        /// </summary>
        public override int GetHashCode()
        {
            var h1 = _segment?.GetHashCode() ?? 0;
            var h2 = _index.GetHashCode();

            var shift5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
            return ((int)shift5 + h1) ^ h2;
        }

        /// <summary>
        /// Applies a positive offset to read-cursor
        /// </summary>
        public static ReadCursor operator +(ReadCursor cursor, int bytes)
        {
            var segment = cursor.Segment;
            var index = cursor.Index;
            while (bytes > 0 && segment != null)
            {
                int remainingThisSegment = segment.End - index;

                // note: if ends at boundary, prefer to return "end of last block" to "start of next"
                // even though kinda semantically identical
                if(bytes <= remainingThisSegment)
                {
                    return new ReadCursor(segment, index + bytes);
                }
                // account for everything left in this segment
                bytes -= remainingThisSegment;

                // move to the next segment
                segment = segment.Next;
                index = segment?.Start ?? 0;
            }
            if (bytes == 0)
            {
                return new ReadCursor(segment, index);
            }
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        /// <summary>
        /// Calcualte the offset between two cursors
        /// </summary>
        /// <remarks>The first operand must be equal or greater than the second operand -
        /// meaning: the expected result must be non-negative</remarks>
        public static int operator -(ReadCursor later, ReadCursor earlier)
        {
            var segment = earlier.Segment;
            int index = earlier.Index, delta = 0;
            while(segment != null)
            {
                if(segment == later.Segment)
                {
                    // in the same block, yay!
                    var localDelta = later.Index - index;
                    if(localDelta < 0)
                    {
                        // that means that "earlier" > "later" (same segment)
                        throw new ArgumentException();
                    }

                    return delta + localDelta;
                }
                // account for everything left in this segment
                delta += segment.End - index;

                // move to the next segment
                segment = segment.Next;
                index = segment?.Start ?? 0;
            }

            if(later.IsDefault && earlier.IsDefault)
            {
                return 0; // we'll allow this as a special case
            }

            // we didn't find "later", so either "earlier" < "later" (different segments),
            // or they are from completely unrelated chains
            throw new ArgumentException();
        }
    }
}
