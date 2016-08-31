// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Channels
{
    public struct ReadableBuffer : IDisposable, IEnumerable<BufferSpan>
    {
        private readonly BufferSpan _span;
        private readonly ReadCursor _start;
        private readonly ReadCursor _end;
        private readonly int _length;
        private readonly bool _isOwner;
        private readonly MemoryPoolChannel _channel;
        private bool _disposed;

        public int Length => _length;
        public bool IsEmpty => _length == 0;

        public bool IsSingleSpan => _start.Segment.Block == _end.Segment.Block;

        public BufferSpan FirstSpan => _span;

        public ReadCursor Start => _start;
        public ReadCursor End => _end;

        internal ReadableBuffer(MemoryPoolChannel channel, ReadCursor start, ReadCursor end) :
            this(channel, start, end, isOwner: false)
        {

        }

        internal ReadableBuffer(MemoryPoolChannel channel, ReadCursor start, ReadCursor end, bool isOwner)
        {
            _channel = channel;
            _start = start;
            _end = end;
            _isOwner = isOwner;
            _disposed = false;

            var begin = start;
            begin.TryGetBuffer(end, out _span);

            begin = start;
            _length = begin.GetLength(end);
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
            var begin = start;
            var actual = begin.Seek(length);
            return Slice(begin, actual);
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

            return FirstSpan.Array[FirstSpan.Offset];
        }

        public ReadableBuffer Preserve()
        {
            var begin = _start;
            var end = _end;

            var segmentHead = MemoryBlockSegment.Clone(begin, end);
            var segmentTail = segmentHead;

            while (segmentTail.Next != null)
            {
                segmentTail = segmentTail.Next;
            }

            begin = new ReadCursor(segmentHead);
            end = new ReadCursor(segmentTail, segmentTail.End);

            return new ReadableBuffer(_channel, begin, end, isOwner: true);
        }

        public unsafe void CopyTo(byte* destination, int length)
        {
            if (length < Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            var remaining = (uint)Length;
            foreach (var span in this)
            {
                if (remaining == 0)
                {
                    break;
                }

                var count = (uint)Math.Min(remaining, span.Length);
                Unsafe.CopyBlock(destination, (byte*)span.BufferPtr, count);
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

            foreach (var span in this)
            {
                Buffer.BlockCopy(span.Array, span.Offset, data, offset, span.Length);
                offset += span.Length;
            }
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
