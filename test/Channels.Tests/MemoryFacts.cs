using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Channels.Tests
{
    public class MemoryFacts
    {
        [Fact]
        public void UnsafePointerThrowsIfNotPinned()
        {
            var data = new byte[10];
            var memory = new Memory<byte>(data, 0, data.Length);

            Assert.Throws<InvalidOperationException>(() =>
            {
                unsafe
                {
                    var pointer = memory.UnsafePointer;
                }
            });
        }

        [Fact]
        public void UnsafePointerSafeWithoutPinningIfNativeMemory()
        {
            unsafe
            {
                IntPtr raw = Marshal.AllocHGlobal(10);
                var memory = new Memory<byte>((void*)raw, 10);
                Assert.True((void*)raw == memory.UnsafePointer);
                Marshal.FreeHGlobal(raw);
            }
        }

        [Fact]
        public void UnsafePointerDoesNotThrowIfArrayAndPrePinned()
        {
            unsafe
            {
                var data = new byte[10];
                fixed (byte* ptr = data)
                {
                    var memory = new Memory<byte>(data, 0, data.Length, isPinned: true);
                    Assert.True(ptr == memory.UnsafePointer);
                }
            }
        }

        [Fact]
        public void SliceArrayBackedMemory()
        {
            var data = new byte[10];

            for (int i = 0; i < 10; i++)
            {
                data[i] = (byte)i;
            }

            var memory = new Memory<byte>(data, 0, data.Length);
            var slice = memory.Slice(0, 5);
            var span = slice.Span;
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(i, span[i]);
            }

            var subSlice = slice.Slice(2, 1);
            Assert.Equal(2, subSlice.Span[0]);
        }

        [Fact]
        public void SlicePointerBackedMemory()
        {
            unsafe
            {
                var data = new byte[10];
                for (int i = 0; i < 10; i++)
                {
                    data[i] = (byte)i;
                }

                fixed (byte* ptr = data)
                {
                    var memory = new Memory<byte>(ptr, data.Length);
                    var slice = memory.Slice(0, 5);
                    var span = slice.Span;
                    for (int i = 0; i < 5; i++)
                    {
                        Assert.Equal(i, span[i]);
                    }

                    var subSlice = slice.Slice(2, 1);
                    Assert.Equal(2, subSlice.Span[0]);
                }
            }
        }

        [Fact]
        public unsafe void IsSliceOf_PointerVersusArraySpansNeverMatch()
        {
            ulong[] data = new ulong[8];
            fixed (ulong* ptr = data)
            {
                Memory<ulong> arrayBased = new Memory<ulong>(data, 0, data.Length);
                Memory<ulong> pointerBased = new Memory<ulong>(ptr, 0, 8);

                // comparing array to pointer says "no"
                Assert.False(arrayBased.IsSliceOf(pointerBased));
                Assert.False(pointerBased.IsSliceOf(arrayBased));

                // comparing to self says yes
                Assert.True(arrayBased.IsSliceOf(arrayBased));
                Assert.True(pointerBased.IsSliceOf(pointerBased));

            }
        }

        [Fact]
        public unsafe void IsSliceOf_NonOverlappingSpansNeverMatch()
        {
            ulong[] data = new ulong[8];
            fixed (ulong* ptr = data)
            {
                Memory<ulong> arrayBased = new Memory<ulong>(data, 0, data.Length);
                var x = arrayBased.Slice(1, 2);
                var y = arrayBased.Slice(5, 2);
                Assert.False(x.IsSliceOf(y));
                Assert.False(y.IsSliceOf(x));

                Memory<ulong> pointerBased = new Memory<ulong>(ptr, 0, 8);
                x = pointerBased.Slice(1, 2);
                y = pointerBased.Slice(5, 2);
                Assert.False(x.IsSliceOf(y));
                Assert.False(y.IsSliceOf(x));
            }
        }

        [Fact]
        public unsafe void IsSliceOf_PartialOverlappingSpansNeverMatch()
        {
            ulong[] data = new ulong[8];
            fixed (ulong* ptr = data)
            {
                Memory<ulong> arrayBased = new Memory<ulong>(data, 0, data.Length);
                var x = arrayBased.Slice(1, 4);
                var y = arrayBased.Slice(3, 4);
                Assert.False(x.IsSliceOf(y));
                Assert.False(y.IsSliceOf(x));

                Memory<ulong> pointerBased = new Memory<ulong>(ptr, 0, 8);
                x = pointerBased.Slice(1, 4);
                y = pointerBased.Slice(3, 4);
                Assert.False(x.IsSliceOf(y));
                Assert.False(y.IsSliceOf(x));
            }
        }

        [Fact]
        public unsafe void IsSliceOf_EqualSpansAlwaysMatch()
        {
            ulong[] data = new ulong[8];
            fixed (ulong* ptr = data)
            {
                Memory<ulong> arrayBased = new Memory<ulong>(data, 0, data.Length);
                Assert.True(arrayBased.IsSliceOf(arrayBased));
                for (int start = 0; start < 8; start++)
                {
                    for (int length = data.Length - start; length >= 0; length--)
                    {
                        Assert.True(arrayBased.Slice(start, length).IsSliceOf(arrayBased.Slice(start, length)));
                    }
                }

                Memory<ulong> pointerBased = new Memory<ulong>(ptr, 0, 8);
                Assert.True(pointerBased.IsSliceOf(pointerBased));
                for (int start = 0; start < 8; start++)
                {
                    for (int length = data.Length - start; length >= 0; length--)
                    {
                        Assert.True(pointerBased.Slice(start, length).IsSliceOf(pointerBased.Slice(start, length)));
                    }
                }
            }
        }
        [Fact]
        public void IsSliceOf_SubSpansShouldMatch_BasicArrays()
        {
            int[] data = new int[20];

            var outer = new Memory<int>(data, 0, 20);
            var inner = outer.Slice(6, 3);

            Assert.True(outer.IsSliceOf(outer));
            Assert.True(inner.IsSliceOf(inner));

            Assert.False(outer.IsSliceOf(inner));
            Assert.True(inner.IsSliceOf(outer));
            int start;
            Assert.True(inner.IsSliceOf(outer, out start));
            Assert.Equal(6, start);


            var innerInner = inner.Slice(1, inner.Length - 1);
            Assert.True(innerInner.IsSliceOf(innerInner));
            Assert.True(innerInner.IsSliceOf(inner));
            Assert.True(innerInner.IsSliceOf(inner, out start));
            Assert.Equal(1, start);
            Assert.True(innerInner.IsSliceOf(outer));
            Assert.True(innerInner.IsSliceOf(outer, out start));
            Assert.Equal(7, start);
        }

        [Fact]
        public unsafe void IsSliceOf_SubSpansShouldMatch_BasicPointers()
        {
            int* data = stackalloc int[20];

            var outer = new Memory<int>(data, 0, 20);
            var inner = outer.Slice(6, 3);

            Assert.True(outer.IsSliceOf(outer));
            Assert.True(inner.IsSliceOf(inner));

            Assert.False(outer.IsSliceOf(inner));
            Assert.True(inner.IsSliceOf(outer));
            int start;
            Assert.True(inner.IsSliceOf(outer, out start));
            Assert.Equal(6, start);


            var innerInner = inner.Slice(1, inner.Length - 1);
            Assert.True(innerInner.IsSliceOf(innerInner));
            Assert.True(innerInner.IsSliceOf(inner));
            Assert.True(innerInner.IsSliceOf(inner, out start));
            Assert.Equal(1, start);
            Assert.True(innerInner.IsSliceOf(outer));
            Assert.True(innerInner.IsSliceOf(outer, out start));
            Assert.Equal(7, start);
        }


        [Fact]
        public unsafe void IsSliceOf_MisalignedPointersShouldNotMatch()
        {
            byte* data = stackalloc byte[10 * sizeof(ulong)];
            var outer = new Memory<ulong>(data, 0, 10);
            var inner = new Memory<ulong>(data + 8, 0, 1);
            Assert.True(inner.IsSliceOf(outer));

            inner = new Memory<ulong>(data + 11, 0, 1);
            Assert.False(inner.IsSliceOf(outer));
        }
    }
}
