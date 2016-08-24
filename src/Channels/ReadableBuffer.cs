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
        private bool _disposed;

        public ReadIterator Start => _start;
        public ReadIterator End => _end;

        public ReadableBuffer(ReadIterator start, ReadIterator end) :
            this(start, end, isOwner: false)
        {

        }

        public ReadableBuffer(ReadIterator start, ReadIterator end, bool isOwner)
        {
            _start = start;
            _end = end;
            _isOwner = isOwner;
            _disposed = false;

            var begin = start;
            begin.TryGetBuffer(end, out _span);

            begin = start;
            _length = begin.GetLength(end);
        }

        public ReadIterator IndexOf(ref Vector<byte> data)
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
            tail = new ReadIterator(segmentTail, segmentTail.End);

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

            if (_disposed)
            {
                return;
            }

            var returnStart = _start.Segment;
            var returnEnd = _end.Segment;

            while (true)
            {
                var returnSegment = returnStart;
                returnStart = returnStart.Next;
                returnSegment.Dispose();

                if (returnSegment == returnEnd)
                {
                    break;
                }
            }

            _disposed = true;
        }

        public override string ToString()
        {
            return FirstSpan.ToString();
        }
    }
}
