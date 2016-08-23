// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Channels
{
    public struct ReadableBuffer : IDisposable
    {
        public int Length => _length;

        public BufferSpan FirstSpan => _span;

        private readonly BufferSpan _span;
        private readonly ReadIterator _start;
        private readonly ReadIterator _end;
        private readonly int _length;
        private readonly bool _isOwner;

        public ReadIterator Start => _start;
        public ReadIterator End => _end;

        public ReadableBuffer(ReadIterator head, ReadIterator tail) :
            this(head, tail, isOwner: false)
        {

        }

        public ReadableBuffer(ReadIterator head, ReadIterator tail, bool isOwner)
        {
            _start = head;
            _end = tail;
            _isOwner = isOwner;

            var begin = head;
            begin.TryGetBuffer(tail, out _span);

            begin = head;
            _length = begin.GetLength(tail);
        }

        public ReadIterator Seek(ref Vector<byte> data)
        {
            var iter = _start;
            iter.Seek(ref data);
            return iter;
        }

        public ReadableBuffer Slice(int start, int length)
        {
            var begin = _start;
            if (start != 0)
            {
                begin.Seek(start);
            }
            var end = begin;
            end.Seek(length);
            return Slice(begin, end);
        }

        public ReadableBuffer Slice(int start, ReadIterator end)
        {
            var begin = _start;
            if (start != 0)
            {
                begin.Seek(start);
            }
            return Slice(begin, end);
        }

        public ReadableBuffer Slice(ReadIterator start, ReadIterator end)
        {
            return new ReadableBuffer(start, end);
        }

        public ReadableBuffer Slice(ReadIterator start, int length)
        {
            var end = start.Seek(length);
            return Slice(start, end);
        }

        public ReadableBuffer Slice(ReadIterator start)
        {
            return new ReadableBuffer(start, _end);
        }

        public ReadableBuffer Slice(int start)
        {
            var begin = _start;
            begin.Seek(start);
            return new ReadableBuffer(begin, _end);
        }

        public IEnumerable<BufferSpan> GetSpans()
        {
            var head = _start;

            BufferSpan span;
            while (head.TryGetBuffer(_end, out span))
            {
                yield return span;
            }
        }

        public int Peek()
        {
            return _start.Peek();
        }

        public ReadableBuffer Clone()
        {
            var head = _start;
            var tail = _end;

            var segmentHead = MemoryBlockSegment.Clone(head, tail);
            var segmentTail = segmentHead;

            while (segmentTail.Next != null)
            {
                segmentTail = segmentTail.Next;
            }

            head = new ReadIterator(segmentHead);
            tail = new ReadIterator(segmentTail);

            return new ReadableBuffer(head, tail, isOwner: true);
        }

        public void CopyTo(byte[] data, int offset)
        {
            if (data.Length < Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            var iter = _start;
            iter.CopyTo(data, offset, Length);
        }

        public void Dispose()
        {
            if (!_isOwner)
            {
                return;
            }

            var returnStart = _start.Segment;
            var returnEnd = _end.Segment;

            while (returnStart != returnEnd)
            {
                var returnSegment = returnStart;
                returnStart = returnStart.Next;
                returnSegment.Dispose();
            }
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(FirstSpan.Array, FirstSpan.Offset, FirstSpan.Length);
        }
    }
}
