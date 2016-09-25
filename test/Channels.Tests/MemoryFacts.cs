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
    }
}
