using Channels.Text.Primitives;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                    writeBuffer.Write(data);
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
                writeBuffer.Write(data);
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
                writeBuffer.Advance(50); // not even going to pretend to write data here - we're going to cheat
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
                writeBuffer.Write(bytes);
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                var trimmed = buffer.TrimStart();
                var outputBytes = trimmed.ToArray();

                Assert.Equal(expected, Encoding.ASCII.GetString(outputBytes));
            }
        }

        [Theory]
        [InlineData("foo\rbar\r\n", "\r\n", "foo\rbar")]
        [InlineData("foo\rbar\r\n", "\rbar", "foo")]
        [InlineData("/pathpath/", "path/", "/path")]
        [InlineData("hellzhello", "hell", null)]
        public async Task TrySliceToSpan(string input, string sliceTo, string expected)
        {
            var sliceToBytes = Encoding.UTF8.GetBytes(sliceTo);

            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();

                var writeBuffer = channel.Alloc();
                var bytes = Encoding.UTF8.GetBytes(input);
                writeBuffer.Write(bytes);
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                ReadableBuffer slice;
                ReadCursor cursor;
                Assert.True(buffer.TrySliceTo(sliceToBytes, out slice, out cursor));
                Assert.Equal(expected, slice.GetUtf8String());
            }
        }

        private unsafe void TestIndexOfWorksForAllLocations(ref ReadableBuffer readBuffer, byte emptyValue)
        {
            byte huntValue = (byte)~emptyValue;

            // we're going to fully index the final locations of the buffer, so that we
            // can mutate etc in constant time
            var addresses = BuildPointerIndex(ref readBuffer);

            // check it isn't there to start with
            ReadableBuffer slice;
            ReadCursor cursor;
            var found = readBuffer.TrySliceTo(huntValue, out slice, out cursor);
            Assert.False(found);

            // correctness test all values 
            for (int i = 0; i < readBuffer.Length; i++)
            {
                *addresses[i] = huntValue;
                found = readBuffer.TrySliceTo(huntValue, out slice, out cursor);
                *addresses[i] = emptyValue;

                Assert.True(found);
                var remaining = readBuffer.Slice(cursor);
                Assert.True((byte*)remaining.First.UnsafePointer == addresses[i]);
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
            var ptr = (byte*)readBuffer.First.UnsafePointer;
            string s = value.ToString(CultureInfo.InvariantCulture);
            int written;
            fixed (char* c = s)
            {
                written = Encoding.ASCII.GetBytes(c, s.Length, ptr, readBuffer.Length);
            }
            var slice = readBuffer.Slice(0, written);
            Assert.Equal(value, slice.GetUInt64());
        }

        [Theory]
        [InlineData("abc,def,ghi", ',')]
        [InlineData("a;b;c;d", ';')]
        [InlineData("a;b;c;d", ',')]
        [InlineData("", ',')]
        public Task Split(string input, char delimiter)
        {
            // note: different expectation to string.Split; empty has 0 outputs
            var expected = input == "" ? new string[0] : input.Split(delimiter);

            using (var channelFactory = new ChannelFactory())
            {
                var channel = channelFactory.CreateChannel();
                var output = channel.Alloc();
                output.WriteUtf8String(input);

                var readable = output.AsReadableBuffer();

                // via struct API
                var iter = readable.Split((byte)delimiter);
                Assert.Equal(expected.Length, iter.Count());
                int i = 0;
                foreach (var item in iter)
                {
                    Assert.Equal(expected[i++], item.GetUtf8String());
                }
                Assert.Equal(expected.Length, i);

                // via objects/LINQ etc
                IEnumerable<ReadableBuffer> asObject = iter;
                Assert.Equal(expected.Length, asObject.Count());
                i = 0;
                foreach (var item in asObject)
                {
                    Assert.Equal(expected[i++], item.GetUtf8String());
                }
                Assert.Equal(expected.Length, i);

                return output.FlushAsync();
            }
        }

        [Fact]
        public async Task ReadTWorksAgainstSimpleBuffers()
        {
            byte[] chunk = { 0, 1, 2, 3, 4, 5, 6, 7 };
            var span = new Span<byte>(chunk);

            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                var output = channel.Alloc();
                output.Write(span);
                var readable = output.AsReadableBuffer();
                Assert.True(readable.IsSingleSpan);
                Assert.Equal(span.Read<byte>(), readable.ReadLittleEndian<byte>());
                Assert.Equal(span.Read<sbyte>(), readable.ReadLittleEndian<sbyte>());
                Assert.Equal(span.Read<short>(), readable.ReadLittleEndian<short>());
                Assert.Equal(span.Read<ushort>(), readable.ReadLittleEndian<ushort>());
                Assert.Equal(span.Read<int>(), readable.ReadLittleEndian<int>());
                Assert.Equal(span.Read<uint>(), readable.ReadLittleEndian<uint>());
                Assert.Equal(span.Read<long>(), readable.ReadLittleEndian<long>());
                Assert.Equal(span.Read<ulong>(), readable.ReadLittleEndian<ulong>());
                Assert.Equal(span.Read<float>(), readable.ReadLittleEndian<float>());
                Assert.Equal(span.Read<double>(), readable.ReadLittleEndian<double>());
                await output.FlushAsync();
            }
        }

        [Fact]
        public async Task ReadTWorksAgainstMultipleBuffers()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                var output = channel.Alloc();

                // we're going to try to force 3 buffers for 8 bytes
                output.Write(new byte[] { 0, 1, 2 });
                output.Ensure(4031);
                output.Write(new byte[] { 3, 4, 5 });
                output.Ensure(4031);
                output.Write(new byte[] { 6, 7, 9 });
                
                var readable = output.AsReadableBuffer();
                Assert.Equal(9, readable.Length);

                int spanCount = 0;
                foreach(var _ in readable)
                {
                    spanCount++;
                }
                Assert.Equal(3, spanCount);

                byte[] local = new byte[9];
                readable.CopyTo(local);
                var span = new Span<byte>(local);
                
                Assert.Equal(span.Read<byte>(), readable.ReadLittleEndian<byte>());
                Assert.Equal(span.Read<sbyte>(), readable.ReadLittleEndian<sbyte>());
                Assert.Equal(span.Read<short>(), readable.ReadLittleEndian<short>());
                Assert.Equal(span.Read<ushort>(), readable.ReadLittleEndian<ushort>());
                Assert.Equal(span.Read<int>(), readable.ReadLittleEndian<int>());
                Assert.Equal(span.Read<uint>(), readable.ReadLittleEndian<uint>());
                Assert.Equal(span.Read<long>(), readable.ReadLittleEndian<long>());
                Assert.Equal(span.Read<ulong>(), readable.ReadLittleEndian<ulong>());
                Assert.Equal(span.Read<float>(), readable.ReadLittleEndian<float>());
                Assert.Equal(span.Read<double>(), readable.ReadLittleEndian<double>());
                await output.FlushAsync();
            }
        }
    }
}
