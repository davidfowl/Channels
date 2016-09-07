using Channels.Text.Primitives;
using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class ReadableBufferFacts
    {
        [Fact]
        public async Task TestIndexOfWorksForAllLocations()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                const int Size = 5 * 4032; // multiple blocks

                // populate with a pile of dummy data
                byte[] data = new byte[512];
                for (int i = 0; i < data.Length; i++) data[i] = 42;
                int totalBytes = 0;
                var writeBuffer = channel.Alloc();
                for (int i = 0; i < Size / data.Length; i++)
                {
                    writeBuffer.Write(new Span<byte>(data, 0, data.Length));
                    totalBytes += data.Length;
                }
                await writeBuffer.FlushAsync();

                // now read it back
                var readBuffer = await channel.ReadAsync();
                Assert.False(readBuffer.IsSingleSpan);
                Assert.Equal(totalBytes, readBuffer.Length);
                TestIndexOfWorksForAllLocations(ref readBuffer, 42);
            }
        }

        [Fact]
        public async Task EqualsDetectsDeltaForAllLocations()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();

                // populate with dummy data
                const int DataSize = 10000;
                byte[] data = new byte[DataSize];
                var rand = new Random(12345);
                rand.NextBytes(data);

                var writeBuffer = channel.Alloc();
                writeBuffer.Write(new Span<byte>(data, 0, data.Length));
                await writeBuffer.FlushAsync();

                // now read it back
                var readBuffer = await channel.ReadAsync();
                Assert.False(readBuffer.IsSingleSpan);
                Assert.Equal(data.Length, readBuffer.Length);

                // check the entire buffer
                EqualsDetectsDeltaForAllLocations(readBuffer, data, 0, data.Length);

                // check the first 32 sub-lengths
                for (int i = 0; i <= 32; i++)
                {
                    var slice = readBuffer.Slice(0, i);
                    EqualsDetectsDeltaForAllLocations(slice, data, 0, i);
                }

                // check the last 32 sub-lengths
                for (int i = 0; i <= 32; i++)
                {
                    var slice = readBuffer.Slice(data.Length - i, i);
                    EqualsDetectsDeltaForAllLocations(slice, data, data.Length - i, i);
                }
            }
        }

        private void EqualsDetectsDeltaForAllLocations(ReadableBuffer slice, byte[] expected, int offset, int length)
        {
            Assert.Equal(length, slice.Length);
            Assert.True(slice.Equals(new Span<byte>(expected, offset, length)));
            // change one byte in buffer, for every position
            for (int i = 0; i < length; i++)
            {
                expected[offset + i] ^= 42;
                Assert.False(slice.Equals(new Span<byte>(expected, offset, length)));
                expected[offset + i] ^= 42;
            }
        }

        [Fact]
        public async Task GetUInt64GivesExpectedValues()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();

                var writeBuffer = channel.Alloc();
                writeBuffer.Ensure(50);
                writeBuffer.CommitBytes(50); // not even going to pretend to write data here - we're going to cheat
                await writeBuffer.FlushAsync(); // by overwriting the buffer in-situ

                // now read it back
                var readBuffer = await channel.ReadAsync();

                ReadUInt64GivesExpectedValues(ref readBuffer);
            }
        }

        [Theory]
        [InlineData(" hello", "hello")]
        [InlineData("    hello", "hello")]
        [InlineData("\r\n hello", "hello")]
        [InlineData("\rhe  llo", "he  llo")]
        [InlineData("\thell o ", "hell o ")]
        public async Task TrimStartTrimsWhitespaceAtStart(string input, string expected)
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();

                var writeBuffer = channel.Alloc();
                var bytes = Encoding.ASCII.GetBytes(input);
                writeBuffer.Write(new Span<byte>(bytes, 0, bytes.Length));
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                var trimmed = buffer.TrimStart();
                var outputBytes = trimmed.ToArray();

                Assert.Equal(expected, Encoding.ASCII.GetString(outputBytes));
            }
        }

        private unsafe void TestIndexOfWorksForAllLocations(ref ReadableBuffer readBuffer, byte emptyValue)
        {
            byte huntValue = (byte)~emptyValue;
            Vector<byte> huntVector = new Vector<byte>(huntValue);

            // we're going to fully index the final locations of the buffer, so that we
            // can mutate etc in constant time
            var addresses = BuildPointerIndex(ref readBuffer);

            // check it isn't there to start with
            var found = readBuffer.IndexOf(ref huntVector);
            Assert.True(found == ReadCursor.NotFound);

            // correctness test all values 
            for (int i = 0; i < readBuffer.Length; i++)
            {
                *addresses[i] = huntValue;
                found = readBuffer.IndexOf(ref huntVector);
                *addresses[i] = emptyValue;

                Assert.True(found != ReadCursor.NotFound);
                var slice = readBuffer.Slice(found);
                Assert.True(slice.FirstSpan.UnsafePointer == addresses[i]);
            }
        }

        private static unsafe byte*[] BuildPointerIndex(ref ReadableBuffer readBuffer)
        {

            byte*[] addresses = new byte*[readBuffer.Length];
            int index = 0;
            foreach (var span in readBuffer)
            {
                byte* ptr = (byte*)span.UnsafePointer;
                for (int i = 0; i < span.Length; i++)
                {
                    addresses[index++] = ptr++;
                }
            }
            return addresses;
        }

        private unsafe void ReadUInt64GivesExpectedValues(ref ReadableBuffer readBuffer)
        {
            Assert.True(readBuffer.IsSingleSpan);

            for (ulong i = 0; i < 1024; i++)
            {
                TestValue(ref readBuffer, i);
            }
            TestValue(ref readBuffer, ulong.MinValue);
            TestValue(ref readBuffer, ulong.MaxValue);

            var rand = new Random(41234);
            // low numbers
            for (int i = 0; i < 10000; i++)
            {
                TestValue(ref readBuffer, (ulong)rand.Next());
            }
            // wider range of numbers
            for (int i = 0; i < 10000; i++)
            {
                ulong x = (ulong)rand.Next(), y = (ulong)rand.Next();
                TestValue(ref readBuffer, (x << 32) | y);
                TestValue(ref readBuffer, (y << 32) | x);
            }
        }

        private unsafe void TestValue(ref ReadableBuffer readBuffer, ulong value)
        {
            byte* ptr = (byte*)readBuffer.FirstSpan.UnsafePointer;
            string s = value.ToString(CultureInfo.InvariantCulture);
            int written;
            fixed (char* c = s)
            {
                written = Encoding.ASCII.GetBytes(c, s.Length, ptr, readBuffer.Length);
            }
            var slice = readBuffer.Slice(0, written);
            Assert.Equal(value, slice.GetUInt64());
        }
    }
}
