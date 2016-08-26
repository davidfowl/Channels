// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace Channels
{
    public struct ReadableBuffer : IDisposable, IEnumerable<BufferSpan>
    {
        private readonly BufferSpan _span;
        private readonly ReadIterator _start;
        private readonly ReadIterator _end;
        private readonly int _length;
        private readonly bool _isOwner;
        private bool _disposed;

        public int Length => _length;
        public bool IsEmpty => _length == 0;

        public bool IsSingleSpan => _start.Segment.Block == _end.Segment.Block;

        public BufferSpan FirstSpan => _span;

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

        public ReadIterator IndexOf(ref Vector<byte> byte0Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector);
            return begin;
        }

        public ReadIterator IndexOfAny(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector, ref byte1Vector);
            return begin;
        }

        public ReadIterator IndexOfAny(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector, ref Vector<byte> byte2Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector, ref byte1Vector, ref byte2Vector);
            return begin;
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
            var begin = start;
            var actual = begin.Seek(length);
            return Slice(begin, actual);
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

        public int Peek()
        {
            return _start.Peek();
        }

        public ReadableBuffer Clone()
        {
            var begin = _start;
            var end = _end;

            var segmentHead = MemoryBlockSegment.Clone(begin, end);
            var segmentTail = segmentHead;

            while (segmentTail.Next != null)
            {
                segmentTail = segmentTail.Next;
            }

            begin = new ReadIterator(segmentHead);
            end = new ReadIterator(segmentTail, segmentTail.End);

            return new ReadableBuffer(begin, end, isOwner: true);
        }

        public void CopyTo(byte[] data)
        {
            CopyTo(data, 0);
        }

        public void CopyTo(byte[] data, int offset)
        {
            if (data.Length < Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            var begin = _start;
            begin.CopyTo(data, offset, Length);
        }

        public byte[] ToArray()
        {
            var buffer = new byte[Length];
            CopyTo(buffer);
            return buffer;
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

        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        IEnumerator<BufferSpan> IEnumerable<BufferSpan>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<BufferSpan>
        {
            private ReadableBuffer _buffer;
            private BufferSpan _current;

            public Enumerator(ref ReadableBuffer buffer)
            {
                _buffer = buffer;
                _current = default(BufferSpan);
            }

            public BufferSpan Current => _current;

            object IEnumerator.Current
            {
                get { return _current; }
            }

            public void Dispose()
            {

            }

            public bool MoveNext()
            {
                var start = _buffer.Start;
                bool moved = start.TryGetBuffer(_buffer.End, out _current);
                _buffer = _buffer.Slice(start);
                return moved;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }
    }
}
