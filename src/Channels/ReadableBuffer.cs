﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace Channels
{
    public struct ReadableBuffer : IDisposable, IEnumerable<Span<byte>>
    {
        private readonly Span<byte> _span;
        private readonly bool _isOwner;
        private readonly Channel _channel;

        private ReadCursor _start;
        private ReadCursor _end;
        private int _length;

        public int Length => _length >= 0 ? _length : GetLength();
        public bool IsEmpty => Length == 0;

        public bool IsSingleSpan => _start.Segment.Block == _end.Segment.Block;

        public Span<byte> FirstSpan => _span;

        public ReadCursor Start => _start;
        public ReadCursor End => _end;

        internal ReadableBuffer(Channel channel, ReadCursor start, ReadCursor end) :
            this(channel, start, end, isOwner: false)
        {

        }

        internal ReadableBuffer(Channel channel, ReadCursor start, ReadCursor end, bool isOwner)
        {
            _channel = channel;
            _start = start;
            _end = end;
            _isOwner = isOwner;

            var begin = start;
            begin.TryGetBuffer(end, out _span);

            _length = -1;
        }

        private ReadableBuffer(ref ReadableBuffer buffer)
        {
            _channel = buffer._channel;

            var begin = buffer._start;
            var end = buffer._end;

            MemoryBlockSegment segmentTail;
            var segmentHead = MemoryBlockSegment.Clone(begin, end, out segmentTail);

            begin = new ReadCursor(segmentHead);
            end = new ReadCursor(segmentTail, segmentTail.End);

            _start = begin;
            _end = end;
            _isOwner = true;
            _span = buffer._span;

            _length = buffer._length;
        }

        public ReadCursor IndexOf(ref Vector<byte> byte0Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector);
            return begin;
        }

        public ReadCursor IndexOfAny(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector)
        {
            var begin = _start;
            begin.Seek(ref byte0Vector, ref byte1Vector);
            return begin;
        }

        public ReadCursor IndexOfAny(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector, ref Vector<byte> byte2Vector)
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

        public ReadableBuffer Slice(int start, ReadCursor end)
        {
            var begin = _start;
            if (start != 0)
            {
                begin.Seek(start);
            }
            return Slice(begin, end);
        }

        public ReadableBuffer Slice(ReadCursor start, ReadCursor end)
        {
            return new ReadableBuffer(_channel, start, end);
        }

        public ReadableBuffer Slice(ReadCursor start, int length)
        {
            var end = start;
            end.Seek(length);
            return Slice(start, end);
        }

        public ReadableBuffer Slice(ReadCursor start)
        {
            return new ReadableBuffer(_channel, start, _end);
        }

        public ReadableBuffer Slice(int start)
        {
            var begin = _start;
            begin.Seek(start);
            return new ReadableBuffer(_channel, begin, _end);
        }

        public int Peek()
        {
            if (IsEmpty)
            {
                return -1;
            }

            var span = FirstSpan;
            return span[0];
        }

        public ReadableBuffer Preserve()
        {
            return new ReadableBuffer(ref this);
        }

        public unsafe void CopyTo(byte* data, int length)
        {
            if (length < Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            var destination = new Span<byte>(data, length);

            var remaining = (uint)Length;
            foreach (var span in this)
            {
                if (remaining == 0)
                {
                    break;
                }

                var count = (uint)Math.Min(remaining, span.Length);
                var src = span.Slice(remaining);
                src.TryCopyTo(destination);
                destination = destination.Slice(count);
                remaining -= count;
            }
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

            var destination = new Span<byte>(data, offset, data.Length - offset);

            foreach (var span in this)
            {
                span.TryCopyTo(destination);
                destination = destination.Slice(span.Length);
            }
        }

        public byte[] ToArray()
        {
            var buffer = new byte[Length];
            CopyTo(buffer);
            return buffer;
        }

        private int GetLength()
        {
            var begin = _start;
            var length = begin.GetLength(_end);
            _length = length;
            return length;
        }

        public void Dispose()
        {
            if (!_isOwner)
            {
                return;
            }

            var returnStart = _start.Segment;
            var returnEnd = _end.Segment;

            while (true)
            {
                var returnSegment = returnStart;
                returnStart = returnStart?.Next;
                returnSegment?.Dispose();

                if (returnSegment == returnEnd)
                {
                    break;
                }
            }

            _start = default(ReadCursor);
            _end = default(ReadCursor);
        }

        public void Consumed()
        {
            _channel.EndRead(End, End);
        }

        public void Consumed(ReadCursor consumed)
        {
            _channel.EndRead(consumed, consumed);
        }

        public void Consumed(ReadCursor consumed, ReadCursor examined)
        {
            _channel.EndRead(consumed, examined);
        }

        public override string ToString()
        {
            return FirstSpan.ToString();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        IEnumerator<Span<byte>> IEnumerable<Span<byte>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<Span<byte>>
        {
            private ReadableBuffer _buffer;
            private Span<byte> _current;

            public Enumerator(ref ReadableBuffer buffer)
            {
                _buffer = buffer;
                _current = default(Span<byte>);
            }

            public Span<byte> Current => _current;

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
