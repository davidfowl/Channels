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
        public void UnsafePointerDoesNotThrowIfPinned()
        {
            var data = new byte[10];
            var memory = new Memory<byte>(data, 0, data.Length);
            memory.Pin();
            unsafe
            {
                Assert.True(Unsafe.AsPointer(ref data[0]) == memory.UnsafePointer);
            }
        }

        [Fact]
        public void UnsafePointerSafeWithoutPinningIfNativeMemory()
        {
            unsafe
            {
                IntPtr raw = Marshal.AllocHGlobal(10);
                var memory = new Memory<byte>((void*)raw, 0, 10);
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
                    var memory = new Memory<byte>(ptr, 0, data.Length);
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
    }
}
