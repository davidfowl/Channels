// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;

namespace Channels
{
    // TODO: Pool segments
    internal class BufferSegment : IDisposable
    {
        private readonly IBuffer _buffer;
        private readonly Memory<byte> _data;

        public BufferSegment(IBuffer buffer)
        {
            _buffer = buffer;
            _data = buffer.Data;
        }

        public BufferSegment(IBuffer buffer, int offset, int length)
        {
            _buffer = buffer.Preserve();
            _data = _buffer.Data.Slice(offset, length);

            ReadOnly = true;
        }

        public Memory<byte> Data => _data;

        public bool ReadOnly { get; }

        public BufferSegment Next;

        public int Length => Data.Length;

        public void Dispose()
        {
            _buffer.Dispose();
        }

        /// <summary>
        /// ToString overridden for debugger convenience. This displays the "active" byte information in this block as ASCII characters.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            var data = Data.Span;

            for (int i = 0; i < Length; i++)
            {
                builder.Append((char)data[i]);
            }
            return builder.ToString();
        }

        public static BufferSegment Clone(ReadCursor begin, ReadCursor end, out BufferSegment lastSegment)
        {
            var beginSegment = begin.Segment;
            var endSegment = end.Segment;

            if (beginSegment == endSegment)
            {
                lastSegment = new BufferSegment(beginSegment._buffer, begin.Index, end.Index - begin.Index);
                return lastSegment;
            }

            var beginClone = new BufferSegment(beginSegment._buffer, begin.Index, beginSegment.Length);
            var endClone = beginClone;

            beginSegment = beginSegment.Next;

            while (beginSegment != endSegment)
            {
                endClone.Next = new BufferSegment(beginSegment._buffer, 0, beginSegment.Length);

                endClone = endClone.Next;
                beginSegment = beginSegment.Next;
            }

            lastSegment = new BufferSegment(endSegment._buffer, 0, end.Index);
            endClone.Next = lastSegment;

            return beginClone;
        }
    }
}
