// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Numerics;
using System.Text;

namespace Channels
{
    public struct ReadableBuffer
    {
        private static readonly int _vectorSpan = Vector<byte>.Count;

        private LinkedSegment _segment;
        private int _index;

        internal ReadableBuffer(LinkedSegment segment)
        {
            _segment = segment;
            _index = segment?.Start ?? 0;
        }

        internal LinkedSegment Segment => _segment;

        internal int Index => _index;

        internal bool IsDefault => _segment == null;

        public bool IsEnd
        {
            get
            {
                if (_segment == null)
                {
                    return true;
                }
                else if (_index < _segment.End)
                {
                    return false;
                }
                else
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
            }
        }

        public int Take()
        {
            var segment = _segment;
            if (segment == null)
            {
                return -1;
            }

            var index = _index;
            var wasLastBlock = segment.Next == null;

            if (index < segment.End)
            {
                _index = index + 1;
                return segment.Block.Array[index];
            }

            do
            {
                if (wasLastBlock)
                {
                    return -1;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }

                wasLastBlock = segment.Next == null;

                if (index < segment.End)
                {
                    _segment = segment;
                    _index = index + 1;
                    return segment.Block.Array[index];
                }
            } while (true);
        }

        public void Skip(int bytesToSkip)
        {
            if (_segment == null)
            {
                return;
            }

            var wasLastBlock = _segment.Next == null;
            var following = _segment.End - _index;

            if (following >= bytesToSkip)
            {
                _index += bytesToSkip;
                return;
            }

            var segment = _segment;
            var index = _index;
            while (true)
            {
                if (wasLastBlock)
                {
                    throw new InvalidOperationException("Attempted to skip more bytes than available.");
                }
                else
                {
                    bytesToSkip -= following;
                    segment = segment.Next;
                    index = segment.Start;
                }

                wasLastBlock = segment.Next == null;
                following = segment.End - index;

                if (following >= bytesToSkip)
                {
                    _segment = segment;
                    _index = index + bytesToSkip;
                    return;
                }
            }
        }

        public int Seek()
        {
            return Seek(1);
        }

        public int Seek(int bytes)
        {
            if (_segment == null || IsEnd)
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

        public int Peek()
        {
            var segment = _segment;
            if (segment == null)
            {
                return -1;
            }

            var wasLastBlock = _segment.Next == null;
            var index = _index;

            if (index < segment.End)
            {
                return segment.Block.Array[index];
            }

            do
            {
                if (wasLastBlock)
                {
                    return -1;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }

                wasLastBlock = segment.Next == null;

                if (index < segment.End)
                {
                    return segment.Block.Array[index];
                }
            } while (true);
        }

        public unsafe long PeekLong()
        {
            if (_segment == null)
            {
                return -1;
            }

            var wasLastBlock = _segment.Next == null;

            if (_segment.End - _index >= sizeof(long))
            {
                return *(long*)(_segment.Block.DataFixedPtr + _index);
            }
            else if (wasLastBlock)
            {
                return -1;
            }
            else
            {
                var segmentBytes = _segment.End - _index;
                var nextBytes = sizeof(long) - segmentBytes;

                if (_segment.Next.End - _segment.Next.Start < nextBytes)
                {
                    return -1;
                }

                var segmentLong = *(long*)(_segment.Block.DataFixedPtr + _segment.End - sizeof(long));

                var nextLong = *(long*)(_segment.Next.Block.DataFixedPtr + _segment.Next.Start);

                return (segmentLong >> (sizeof(long) - segmentBytes) * 8) | (nextLong << (sizeof(long) - nextBytes) * 8);
            }
        }

        public unsafe int Seek(ref Vector<byte> byte0Vector)
        {
            if (IsDefault)
            {
                return -1;
            }

            var segment = _segment;
            var index = _index;
            var wasLastBlock = segment.Next == null;
            var following = segment.End - index;
            byte[] array;
            var byte0 = byte0Vector[0];

            while (true)
            {
                while (following == 0)
                {
                    if (wasLastBlock)
                    {
                        _segment = segment;
                        _index = index;
                        return -1;
                    }
                    segment = segment.Next;
                    index = segment.Start;
                    wasLastBlock = segment.Next == null;
                    following = segment.End - index;
                }
                array = segment.Block.Array;
                while (following > 0)
                {
                    // Need unit tests to test Vector path
#if !DEBUG
                    // Check will be Jitted away https://github.com/dotnet/coreclr/issues/1079
                    if (Vector.IsHardwareAccelerated)
                    {
#endif
                    if (following >= _vectorSpan)
                    {
                        var byte0Equals = Vector.Equals(new Vector<byte>(array, index), byte0Vector);

                        if (byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            following -= _vectorSpan;
                            index += _vectorSpan;
                            continue;
                        }

                        _segment = segment;
                        _index = index + FindFirstEqualByte(ref byte0Equals);
                        return byte0;
                    }
                    // Need unit tests to test Vector path
#if !DEBUG
                    }
#endif

                    var pCurrent = (segment.Block.DataFixedPtr + index);
                    var pEnd = pCurrent + following;
                    do
                    {
                        if (*pCurrent == byte0)
                        {
                            _segment = segment;
                            _index = index;
                            return byte0;
                        }
                        pCurrent++;
                        index++;
                    } while (pCurrent < pEnd);

                    following = 0;
                    break;
                }
            }
        }

        public unsafe int Seek(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector)
        {
            if (IsDefault)
            {
                return -1;
            }

            var segment = _segment;
            var index = _index;
            var wasLastBlock = segment.Next == null;
            var following = segment.End - index;
            byte[] array;
            int byte0Index = int.MaxValue;
            int byte1Index = int.MaxValue;
            var byte0 = byte0Vector[0];
            var byte1 = byte1Vector[0];

            while (true)
            {
                while (following == 0)
                {
                    if (wasLastBlock)
                    {
                        _segment = segment;
                        _index = index;
                        return -1;
                    }
                    segment = segment.Next;
                    index = segment.Start;
                    wasLastBlock = segment.Next == null;
                    following = segment.End - index;
                }
                array = segment.Block.Array;
                while (following > 0)
                {

                    // Need unit tests to test Vector path
#if !DEBUG
                    // Check will be Jitted away https://github.com/dotnet/coreclr/issues/1079
                    if (Vector.IsHardwareAccelerated)
                    {
#endif
                    if (following >= _vectorSpan)
                    {
                        var data = new Vector<byte>(array, index);
                        var byte0Equals = Vector.Equals(data, byte0Vector);
                        var byte1Equals = Vector.Equals(data, byte1Vector);

                        if (!byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            byte0Index = FindFirstEqualByte(ref byte0Equals);
                        }
                        if (!byte1Equals.Equals(Vector<byte>.Zero))
                        {
                            byte1Index = FindFirstEqualByte(ref byte1Equals);
                        }

                        if (byte0Index == int.MaxValue && byte1Index == int.MaxValue)
                        {
                            following -= _vectorSpan;
                            index += _vectorSpan;
                            continue;
                        }

                        _segment = segment;

                        if (byte0Index < byte1Index)
                        {
                            _index = index + byte0Index;
                            return byte0;
                        }

                        _index = index + byte1Index;
                        return byte1;
                    }
                    // Need unit tests to test Vector path
#if !DEBUG
                    }
#endif
                    var pCurrent = (segment.Block.DataFixedPtr + index);
                    var pEnd = pCurrent + following;
                    do
                    {
                        if (*pCurrent == byte0)
                        {
                            _segment = segment;
                            _index = index;
                            return byte0;
                        }
                        if (*pCurrent == byte1)
                        {
                            _segment = segment;
                            _index = index;
                            return byte1;
                        }
                        pCurrent++;
                        index++;
                    } while (pCurrent != pEnd);

                    following = 0;
                    break;
                }
            }
        }

        public unsafe int Seek(ref Vector<byte> byte0Vector, ref Vector<byte> byte1Vector, ref Vector<byte> byte2Vector)
        {
            if (IsDefault)
            {
                return -1;
            }

            var segment = _segment;
            var index = _index;
            var wasLastBlock = segment.Next == null;
            var following = segment.End - index;
            byte[] array;
            int byte0Index = int.MaxValue;
            int byte1Index = int.MaxValue;
            int byte2Index = int.MaxValue;
            var byte0 = byte0Vector[0];
            var byte1 = byte1Vector[0];
            var byte2 = byte2Vector[0];

            while (true)
            {
                while (following == 0)
                {
                    if (wasLastBlock)
                    {
                        _segment = segment;
                        _index = index;
                        return -1;
                    }
                    segment = segment.Next;
                    index = segment.Start;
                    wasLastBlock = segment.Next == null;
                    following = segment.End - index;
                }
                array = segment.Block.Array;
                while (following > 0)
                {
                    // Need unit tests to test Vector path
#if !DEBUG
                    // Check will be Jitted away https://github.com/dotnet/coreclr/issues/1079
                    if (Vector.IsHardwareAccelerated)
                    {
#endif
                    if (following >= _vectorSpan)
                    {
                        var data = new Vector<byte>(array, index);
                        var byte0Equals = Vector.Equals(data, byte0Vector);
                        var byte1Equals = Vector.Equals(data, byte1Vector);
                        var byte2Equals = Vector.Equals(data, byte2Vector);

                        if (!byte0Equals.Equals(Vector<byte>.Zero))
                        {
                            byte0Index = FindFirstEqualByte(ref byte0Equals);
                        }
                        if (!byte1Equals.Equals(Vector<byte>.Zero))
                        {
                            byte1Index = FindFirstEqualByte(ref byte1Equals);
                        }
                        if (!byte2Equals.Equals(Vector<byte>.Zero))
                        {
                            byte2Index = FindFirstEqualByte(ref byte2Equals);
                        }

                        if (byte0Index == int.MaxValue && byte1Index == int.MaxValue && byte2Index == int.MaxValue)
                        {
                            following -= _vectorSpan;
                            index += _vectorSpan;
                            continue;
                        }

                        _segment = segment;

                        int toReturn, toMove;
                        if (byte0Index < byte1Index)
                        {
                            if (byte0Index < byte2Index)
                            {
                                toReturn = byte0;
                                toMove = byte0Index;
                            }
                            else
                            {
                                toReturn = byte2;
                                toMove = byte2Index;
                            }
                        }
                        else
                        {
                            if (byte1Index < byte2Index)
                            {
                                toReturn = byte1;
                                toMove = byte1Index;
                            }
                            else
                            {
                                toReturn = byte2;
                                toMove = byte2Index;
                            }
                        }

                        _index = index + toMove;
                        return toReturn;
                    }
                    // Need unit tests to test Vector path
#if !DEBUG
                    }
#endif
                    var pCurrent = (segment.Block.DataFixedPtr + index);
                    var pEnd = pCurrent + following;
                    do
                    {
                        if (*pCurrent == byte0)
                        {
                            _segment = segment;
                            _index = index;
                            return byte0;
                        }
                        if (*pCurrent == byte1)
                        {
                            _segment = segment;
                            _index = index;
                            return byte1;
                        }
                        if (*pCurrent == byte2)
                        {
                            _segment = segment;
                            _index = index;
                            return byte2;
                        }
                        pCurrent++;
                        index++;
                    } while (pCurrent != pEnd);

                    following = 0;
                    break;
                }
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

        /// <summary>
        /// Save the data at the current location then move to the next available space.
        /// </summary>
        /// <param name="data">The byte to be saved.</param>
        /// <returns>true if the operation successes. false if can't find available space.</returns>
        public bool Put(byte data)
        {
            if (_segment == null)
            {
                return false;
            }

            var segment = _segment;
            var index = _index;
            while (true)
            {
                var wasLastBlock = segment.Next == null;

                if (index < segment.End)
                {
                    _segment = segment;
                    _index = index + 1;
                    segment.Block.Array[index] = data;
                    return true;
                }
                else if (wasLastBlock)
                {
                    return false;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }
            }
        }

        public int GetLength(ReadableBuffer end)
        {
            if (IsDefault || end.IsDefault)
            {
                return -1;
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
                        throw new InvalidOperationException("end did not follow iterator");
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

        public bool TryGetBuffer(out BufferSpan span)
        {
            return TryGetBuffer(end: default(ReadableBuffer), span: out span);
        }

        public bool TryGetBuffer(ReadableBuffer end, out BufferSpan span)
        {
            span = default(BufferSpan);

            if (IsDefault)
            {
                return false;
            }

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
                    return false;
                }
                else
                {
                    segment = segment.Next;
                    index = segment.Start;
                }
            }

            span = new BufferSpan(segment.Block.DataArrayPtr, segment.Block.Array, index, following);

            _segment = segment;
            _index = index + following;
            return true;
        }

        public int Read(byte[] array, int offset, int count)
        {
            if (IsDefault)
            {
                return 0;
            }

            var actual = 0;
            var segment = _segment;
            var index = _index;
            var remaining = count;
            while (true)
            {
                // Determine if we might attempt to copy data from segment.Next before
                // calculating "following" so we don't risk skipping data that could
                // be added after segment.End when we decide to copy from segment.Next.
                // segment.End will always be advanced before segment.Next is set.
                var wasLastBlock = segment.Next == null;
                var following = segment.End - index;
                if (remaining <= following)
                {
                    actual = count;
                    if (array != null)
                    {
                        Buffer.BlockCopy(segment.Block.Array, index, array, offset, remaining);
                    }

                    _segment = segment;
                    _index = index + remaining;
                    return actual;
                }
                else if (wasLastBlock)
                {
                    actual = count - remaining + following;
                    if (array != null)
                    {
                        Buffer.BlockCopy(segment.Block.Array, index, array, offset, following);
                    }
                    _segment = segment;
                    _index = following;
                    return actual;
                }
                else
                {
                    if (array != null)
                    {
                        Buffer.BlockCopy(segment.Block.Array, index, array, offset, following);
                    }
                    offset += following;
                    remaining -= following;
                    segment = segment.Next;
                    index = segment.Start;
                }
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < (Segment.End - Index); i++)
            {
                if (i > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(Segment.Block.Array[i + Index].ToString("X2"));
            }
            return builder.ToString();
        }
    }
}
