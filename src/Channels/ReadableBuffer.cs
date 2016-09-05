// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Channels
{
    public struct ReadableBuffer : IDisposable, IEnumerable<Span<byte>>
    {
        private static readonly int VectorWidth = Vector<byte>.Count;
        private static readonly int VectorShift = (int)Math.Log(VectorWidth, 2);
        private static readonly int VectorRemainderMask = ~(~0 << VectorShift);

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
            if (IsEmpty)
            {
                return _end;
            }

            byte byte0 = byte0Vector[0];

            var start = _start;
            var seek = 0;

            foreach (var span in this)
            {
                var currentSpan = span;
                var found = false;

                if (Vector.IsHardwareAccelerated)
                {
                    while (currentSpan.Length >= VectorWidth)
                    {
                        var data = currentSpan.Read<Vector<byte>>();
                        var byte0Equals = Vector.Equals(data, byte0Vector);

                        if (byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            currentSpan = currentSpan.Slice(VectorWidth);
                            seek += VectorWidth;
                        }
                        else
                        {
                            var index = FindFirstEqualByte(ref byte0Equals);
                            seek += index;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    // Slow search
                    for (int i = 0; i < currentSpan.Length; i++)
                    {
                        if (currentSpan[i] == byte0)
                        {
                            seek += i;
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                {
                    // TODO: Avoid extra seek
                    start.Seek(seek);
                    return start;
                }
            }

            return _end;
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
            if (start == 0) return this;

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
            var sb = new StringBuilder();
            foreach (var span in this)
            {
                foreach (var b in span)
                {
                    sb.Append((char)b);
                }
            }
            return sb.ToString();
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
            internal ReadCursor _cursor;

            public Enumerator(ref ReadableBuffer buffer)
            {
                _buffer = buffer;
                _current = default(Span<byte>);
                _cursor = default(ReadCursor);
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
                _cursor = _buffer.Start;
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

        public unsafe bool StartsWith(byte[] value, int offset, int count)
        {
            if (Length < count)
            {
                // just nope
                return false;
            }

            fixed (byte* basePtr = value)
            {
                var ptr = basePtr + offset;
                if (IsSingleSpan)
                {
                    return Equals((byte*)FirstSpan.UnsafePointer, ptr, count);
                }

                foreach (var span in this)
                {
                    int batch = Math.Min(span.Length, count);
                    if (!Equals((byte*)span.UnsafePointer, ptr, batch))
                    {
                        return false;
                    }
                    ptr += batch;
                    count -= batch;
                    if (count == 0)
                    {
                        // checked what we wanted; ignore any remainder
                        break;
                    }
                }

                return true;
            }
        }

        public unsafe bool Equals(byte[] value, int offset, int count)
        {
            return Length == count && StartsWith(value, offset, count);
        }

        private static unsafe bool Equals(byte* a, byte* b, int count)
        {
            switch (count)
            {
                case 0: return true;
                // special-case 1-7 - can't use qword checks
                case 1: return *a == *b;
                case 2: return *(ushort*)a == *(ushort*)b;
                case 3: return *(ushort*)a == *(ushort*)b && a[2] == b[2];
                case 4: return *(uint*)a == *(uint*)b;
                case 5: return *(uint*)a == *(uint*)b && a[4] == b[4];
                case 6: return *(uint*)a == *(uint*)b && *(ushort*)(a + 4) == *(ushort*)(b + 4);
                case 7: return *(uint*)a == *(uint*)b && *(uint*)(a + 3) == *(uint*)(b + 3);
                // special case a single qword, for simplicity and performance
                case 8: return *(ulong*)a == *(ulong*)b;
                default:
                    int chunks;
                    if (Vector.IsHardwareAccelerated && (chunks = count >> VectorShift) != 0)
                    {
                        do
                        {
                            var vecA = Unsafe.Read<Vector<byte>>(a);
                            var vecB = Unsafe.Read<Vector<byte>>(b);
                            if (!vecA.Equals(vecB))
                            {
                                return false;
                            }

                            a += VectorWidth;
                            b += VectorWidth;
                        } while (--chunks != 0);

                        count &= VectorRemainderMask;
                    }

                    // qword chunks
                    chunks = count >> 3;
                    if (chunks != 0)
                    {
                        ulong* a8 = (ulong*)a, b8 = (ulong*)b;
                        do
                        {
                            if (*a8++ != *b8++)
                            {
                                return false;
                            }
                        }
                        while (--chunks != 0);
                        a = (byte*)a8;
                        b = (byte*)b8;
                    }

                    // if we're checking a multiple of 8, we're done
                    if ((count & 7) == 0)
                    {
                        return true;
                    }

                    // now check the last qword, noting that we have enough space
                    // to look backwards (because we're in the > 8 default case)
                    int delta = 8 - (count & 7);
                    return *(ulong*)(a - delta) == *(ulong*)(b - delta);
            }
        }

        /// <summary>
        /// Find first byte
        /// </summary>
        /// <param  name="byteEquals"></param >
        /// <returns>The first index of the result vector</returns>
        /// <exception cref="InvalidOperationException">byteEquals = 0</exception>
        internal static int FindFirstEqualByte(ref Vector<byte> byteEquals)
        {
            if (!BitConverter.IsLittleEndian) return FindFirstEqualByteSlow(ref byteEquals);

            // Quasi-tree search
            var vector64 = Vector.AsVectorInt64(byteEquals);
            for (var i = 0; i < Vector<long>.Count; i++)
            {
                var longValue = vector64[i];
                if (longValue == 0) continue;

                return (i << 3) +
                    ((longValue & 0x00000000ffffffff) > 0
                        ? (longValue & 0x000000000000ffff) > 0
                            ? (longValue & 0x00000000000000ff) > 0 ? 0 : 1
                            : (longValue & 0x0000000000ff0000) > 0 ? 2 : 3
                        : (longValue & 0x0000ffff00000000) > 0
                            ? (longValue & 0x000000ff00000000) > 0 ? 4 : 5
                            : (longValue & 0x00ff000000000000) > 0 ? 6 : 7);
            }
            throw new InvalidOperationException();
        }

        // Internal for testing
        internal static int FindFirstEqualByteSlow(ref Vector<byte> byteEquals)
        {
            // Quasi-tree search
            var vector64 = Vector.AsVectorInt64(byteEquals);
            for (var i = 0; i < Vector<long>.Count; i++)
            {
                var longValue = vector64[i];
                if (longValue == 0) continue;

                var shift = i << 1;
                var offset = shift << 2;
                var vector32 = Vector.AsVectorInt32(byteEquals);
                if (vector32[shift] != 0)
                {
                    if (byteEquals[offset] != 0) return offset;
                    if (byteEquals[offset + 1] != 0) return offset + 1;
                    if (byteEquals[offset + 2] != 0) return offset + 2;
                    return offset + 3;
                }
                if (byteEquals[offset + 4] != 0) return offset + 4;
                if (byteEquals[offset + 5] != 0) return offset + 5;
                if (byteEquals[offset + 6] != 0) return offset + 6;
                return offset + 7;
            }
            throw new InvalidOperationException();
        }
    }
}
