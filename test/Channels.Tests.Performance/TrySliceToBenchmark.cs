using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Channels.Tests.Performance
{
    [Config(typeof(NoMemoryConfig))]
    public class TrySliceToBenchmark
    {
        private const ulong xorPowerOfTwoToHighByte = (0x07ul       |
                                                       0x06ul <<  8 |
                                                       0x05ul << 16 |
                                                       0x04ul << 24 |
                                                       0x03ul << 32 |
                                                       0x02ul << 40 |
                                                       0x01ul << 48 ) + 1;

        private const ulong byteBroadcastToUlong = ~0UL / byte.MaxValue;
        private const ulong filterByteHighBitsInUlong = (byteBroadcastToUlong >> 1) | (byteBroadcastToUlong << (sizeof(ulong) * 8 - 1));

        [Params(1, 2, 4, 7, 8, 15, 16, 31, 32, 40, 63, 64, 120, 127, 128, 1023, 1024, 1032)]
        public int Size { get; set; }

        private byte[] Array; 

        [Setup]
        public void Setup()
        {
            Array = new byte[Size];

            Array[Size - 1] = (byte) '\r';
        }

        [Benchmark(Baseline = true)]
        public bool NaiveTrySliceTo()
        {
            ReadableBuffer slice;
            ReadCursor cursor;

            return NaiveTrySliceTo((byte)'\r', out slice, out cursor);
        }

        private bool NaiveTrySliceTo(byte b1, out ReadableBuffer slice, out ReadCursor cursor)
        {
            var span = new Span<byte>(Array);

            var found = false;
            var seek = 0;

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == b1)
                {
                    found = true;
                    break;
                }
                seek++;
            }

            if (found)
            {
                cursor = new ReadCursor();
                slice = new ReadableBuffer();
                return true;
            }


            slice = default(ReadableBuffer);
            cursor = default(ReadCursor);
            return false;
        }

        [Benchmark]
        public bool VectorTernaryTrySliceTo()
        {
            ReadableBuffer slice;
            ReadCursor cursor;

            return VectorTernaryTrySliceTo((byte)'\r', out slice, out cursor);
        }

        private bool VectorTernaryTrySliceTo(byte b1, out ReadableBuffer slice, out ReadCursor cursor)
        {
            var span = new Span<byte>(Array);

            var found = false;
            var seek = 0;

            var byte0Vector = GetVector(b1);

            while (span.Length >= Vector<byte>.Count)
            {
                var data = span.Read<Vector<byte>>();

                var byte0Equals = Vector.Equals(data, byte0Vector);
                if (byte0Equals.Equals(Vector<byte>.Zero))
                {
                    span = span.Slice(Vector<byte>.Count);
                    seek += Vector<byte>.Count;
                }
                else
                {
                    var index = FindFirstEqualByte(ref byte0Equals);
                    seek += index;
                    found = true;
                    break;
                }
            }
                

            if (!found)
            {
                // Slow search
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] == b1)
                    {
                        found = true;
                        break;
                    }
                    seek++;
                }
            }

            if (found)
            {
                cursor = new ReadCursor();
                slice = new ReadableBuffer();
                return true;
            }

            slice = default(ReadableBuffer);
            cursor = default(ReadCursor);
            return false;
        }


        [Benchmark]
        public bool VectorMagicTrySliceTo()
        {
            ReadableBuffer slice;
            ReadCursor cursor;

            return VectorMagicTrySliceTo((byte)'\r', out slice, out cursor);
        }

        private bool VectorMagicTrySliceTo(byte b1, out ReadableBuffer slice, out ReadCursor cursor)
        {
            var span = new Span<byte>(Array);

            var found = false;
            var seek = 0;

            var byte0Vector = GetVector(b1);

            // Search by Vector length (16/32/64 bytes)
            while (span.Length >= Vector<byte>.Count)
            {
                var data = span.Read<Vector<byte>>();

                var byte0Equals = Vector.Equals(data, byte0Vector);
                if (byte0Equals.Equals(Vector<byte>.Zero))
                {
                    span = span.Slice(Vector<byte>.Count);
                    seek += Vector<byte>.Count;
                }
                else
                {
                    var index = LocateFirstFoundByte(ref byte0Equals);
                    seek += index;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Byte by byte search
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] == b1)
                    {
                        found = true;
                        break;
                    }
                    seek++;
                }
            }

            if (found)
            {
                cursor = new ReadCursor();
                slice = new ReadableBuffer();
                return true;
            }


            slice = default(ReadableBuffer);
            cursor = default(ReadCursor);
            return false;
        }

        [Benchmark]
        public bool VectorLongMagicTrySliceTo()
        {
            ReadableBuffer slice;
            ReadCursor cursor;

            return VectorLongMagicTrySliceTo((byte)'\r', out slice, out cursor);
        }

        private bool VectorLongMagicTrySliceTo(byte b1, out ReadableBuffer slice, out ReadCursor cursor)
        {
            var span = new Span<byte>(Array);

            var found = false;
            var seek = 0;

            var byte0Vector = GetVector(b1);

            // Search by Vector length (16/32/64 bytes)
            while (span.Length >= Vector<byte>.Count)
            {
                var data = span.Read<Vector<byte>>();

                var byte0Equals = Vector.Equals(data, byte0Vector);
                if (byte0Equals.Equals(Vector<byte>.Zero))
                {
                    span = span.Slice(Vector<byte>.Count);
                    seek += Vector<byte>.Count;
                }
                else
                {
                    var index = LocateFirstFoundByte(ref byte0Equals);
                    seek += index;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Search by Long length (8 bytes)
                while (span.Length >= sizeof(ulong))
                {
                    var data = span.Read<ulong>();

                    var byteEquals = SetLowBitsForByteMatch(data, b1);
                    if (byteEquals == 0)
                    {
                        span = span.Slice(sizeof(ulong));
                        seek += sizeof(ulong);
                    }
                    else
                    {
                        var index = LocateFirstFoundByte(byteEquals);
                        seek += index;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                // Byte by byte search
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] == b1)
                    {
                        found = true;
                        break;
                    }
                    seek++;
                }
            }

            if (found)
            {
                cursor = new ReadCursor();
                slice = new ReadableBuffer();
                return true;
            }


            slice = default(ReadableBuffer);
            cursor = default(ReadCursor);
            return false;
        }

        /// <summary>
        /// Locate the first of the found bytes
        /// </summary>
        /// <param  name="byteEquals"></param >
        /// <returns>The first index of the result vector</returns>
        // Force inlining (64 IL bytes, 91 bytes asm) Issue: https://github.com/dotnet/coreclr/issues/7386
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int LocateFirstFoundByte(ref Vector<byte> byteEquals)
        {
            var vector64 = Vector.AsVectorInt64(byteEquals);
            var i = 0;
            long longValue = 0;
            for (; i < Vector<long>.Count; i++)
            {
                longValue = vector64[i];
                if (longValue == 0) continue;
                break;
            }

            // Single LEA instruction with jitted const (using function result)
            return i * 8 + LocateFirstFoundByte(longValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int LocateFirstFoundByte(long byteEquals)
        {
            // Flag least significant power of two bit
            var powerOfTwoFlag = (ulong)(byteEquals ^ (byteEquals - 1));
            // Shift all powers of two into the high byte and extract
            return (int)((powerOfTwoFlag * xorPowerOfTwoToHighByte) >> 57);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long SetLowBitsForByteMatch(ulong ulongValue, byte search)
        {
            var value = ulongValue ^ (byteBroadcastToUlong * search);
            return (long)(
                (
                    (value - byteBroadcastToUlong) &
                    ~(value) &
                    filterByteHighBitsInUlong
                ) >> 7);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<byte> GetVector(byte vectorByte)
        {
            // Vector<byte> .ctor is a bit fussy to get working; however this always seems to work
            // https://github.com/dotnet/coreclr/issues/7459#issuecomment-253965670
            return Vector.AsVectorByte(new Vector<ulong>(vectorByte * 0x0101010101010101ul));
        }

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
