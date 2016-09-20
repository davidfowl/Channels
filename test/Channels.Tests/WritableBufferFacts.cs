﻿using System;
using System.Text;
using System.Threading.Tasks;
using Channels.Text.Primitives;
using Xunit;

namespace Channels.Tests
{
    public class WritableBufferFacts
    {
        [Fact]
        public async Task CanWriteNothingToBuffer()
        {
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);
                var buffer = channel.Alloc();
                buffer.CommitBytes(0); // doing nothing, the hard way
                await buffer.FlushAsync();
            }
        }

        [Theory]
        [InlineData(1, "1")]
        [InlineData(20, "20")]
        [InlineData(300, "300")]
        [InlineData(4000, "4000")]
        [InlineData(500000, "500000")]
        [InlineData(60000000000000000, "60000000000000000")]
        public async Task CanWriteUInt64ToBuffer(ulong value, string valueAsString)
        {
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);
                var buffer = channel.Alloc();
                buffer.WriteUInt64(value);
                await buffer.FlushAsync();

                var inputBuffer = await channel.ReadAsync();

                Assert.Equal(valueAsString, inputBuffer.GetUtf8String());
            }
        }

        [Theory]
        [InlineData(5)]
        [InlineData(50)]
        [InlineData(500)]
        [InlineData(5000)]
        [InlineData(50000)]
        public async Task WriteLargeDataBinary(int length)
        {
            byte[] data = new byte[length];
            new Random(length).NextBytes(data);
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);

                var output = channel.Alloc();
                output.Write(data);
                var foo = output.Memory.IsEmpty; // trying to see if .Memory breaks
                await output.FlushAsync();
                channel.CompleteWriter();

                int offset = 0;
                while (true)
                {
                    var input = await channel.ReadAsync();
                    if (input.Length == 0) break;

                    Assert.True(input.Equals(new Span<byte>(data, offset, input.Length)));
                    offset += input.Length;
                    channel.Advance(input.End);
                }
                Assert.Equal(data.Length, offset);
            }
        }

        [Theory]
        [InlineData(5)]
        [InlineData(50)]
        [InlineData(500)]
        [InlineData(5000)]
        [InlineData(50000)]
        public async Task WriteLargeDataTextUtf8(int length)
        {
            string data = new string('#', length);
            FillRandomStringData(data, length);
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);

                var output = channel.Alloc();
                output.WriteUtf8String(data);
                var foo = output.Memory.IsEmpty; // trying to see if .Memory breaks
                await output.FlushAsync();
                channel.CompleteWriter();

                int offset = 0;
                while (true)
                {
                    var input = await channel.ReadAsync();
                    if (input.Length == 0) break;

                    string s = ReadableBufferExtensions.GetUtf8String(input);
                    Assert.Equal(data.Substring(offset, input.Length), s);
                    offset += input.Length;
                    channel.Advance(input.End);
                }
                Assert.Equal(data.Length, offset);
            }
        }
        [Theory]
        [InlineData(5)]
        [InlineData(50)]
        [InlineData(500)]
        [InlineData(5000)]
        [InlineData(50000)]
        public async Task WriteLargeDataTextAscii(int length)
        {
            string data = new string('#', length);
            FillRandomStringData(data, length);
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);

                var output = channel.Alloc();
                output.WriteAsciiString(data);
                var foo = output.Memory.IsEmpty; // trying to see if .Memory breaks
                await output.FlushAsync();
                channel.CompleteWriter();

                int offset = 0;
                while (true)
                {
                    var input = await channel.ReadAsync();
                    if (input.Length == 0) break;

                    string s = ReadableBufferExtensions.GetAsciiString(input);
                    Assert.Equal(data.Substring(offset, input.Length), s);
                    offset += input.Length;
                    channel.Advance(input.End);
                }
                Assert.Equal(data.Length, offset);
            }
        }

        private unsafe void FillRandomStringData(string data, int seed)
        {
            Random rand = new Random(seed);
            fixed (char* c = data)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    c[i] = (char)(rand.Next(127) + 1); // want range 1-127
                }
            }
        }


        [Fact]
        public void CanReReadDataThatHasNotBeenFlushed_SmallData()
        {
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);
                var output = channel.Alloc();

                Assert.True(output.AsReadableBuffer().IsEmpty);
                Assert.Equal(0, output.AsReadableBuffer().Length);


                output.WriteUtf8String("hello world");
                var readable = output.AsReadableBuffer();

                // check that looks about right
                Assert.False(readable.IsEmpty);
                Assert.Equal(11, readable.Length);
                Assert.True(readable.Equals(Encoding.UTF8.GetBytes("hello world")));
                Assert.True(readable.Slice(1, 3).Equals(Encoding.UTF8.GetBytes("ell")));

                // check it all works after we write more
                output.WriteUtf8String("more data");

                // note that the snapshotted readable should not have changed by this
                Assert.False(readable.IsEmpty);
                Assert.Equal(11, readable.Length);
                Assert.True(readable.Equals(Encoding.UTF8.GetBytes("hello world")));
                Assert.True(readable.Slice(1, 3).Equals(Encoding.UTF8.GetBytes("ell")));

                // if we fetch it again, we can see everything
                readable = output.AsReadableBuffer();
                Assert.False(readable.IsEmpty);
                Assert.Equal(20, readable.Length);
                Assert.True(readable.Equals(Encoding.UTF8.GetBytes("hello worldmore data")));
            }
        }

        [Fact]
        public void CanReReadDataThatHasNotBeenFlushed_LargeData()
        {
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);

                var output = channel.Alloc();

                byte[] predictablyGibberish = new byte[512];
                const int SEED = 1235412;
                Random random = new Random(SEED);
                for (int i = 0; i < 50; i++)
                {
                    for (int j = 0; j < predictablyGibberish.Length; j++)
                    {
                        // doing it this way to be 100% sure about repeating the PRNG order
                        predictablyGibberish[j] = (byte)random.Next(0, 256);
                    }
                    output.Write(predictablyGibberish);
                }

                var readable = output.AsReadableBuffer();
                Assert.False(readable.IsSingleSpan);
                Assert.False(readable.IsEmpty);
                Assert.Equal(50 * 512, readable.Length);

                random = new Random(SEED);
                int correctCount = 0;
                foreach (var span in readable)
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (span[i] == (byte)random.Next(0, 256)) correctCount++;
                    }
                }
                Assert.Equal(50 * 512, correctCount);
            }
        }

        [Fact]
        public async Task CanAppendSelfWhileEmpty()
        { // not really an expectation; just an accepted caveat
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);

                var output = channel.Alloc();
                var readable = output.AsReadableBuffer();
                output.Append(ref readable);
                Assert.Equal(0, output.AsReadableBuffer().Length);

                await output.FlushAsync();
            }
        }

        [Fact]
        public async Task CanAppendSelfWhileNotEmpty()
        {
            byte[] chunk = new byte[512];
            new Random().NextBytes(chunk);
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);

                var output = channel.Alloc();

                for (int i = 0; i < 20; i++)
                {
                    output.Write(chunk);
                }
                var readable = output.AsReadableBuffer();
                Assert.Equal(512 * 20, readable.Length);

                output.Append(ref readable);
                Assert.Equal(512 * 20, readable.Length);

                readable = output.AsReadableBuffer();
                Assert.Equal(2 * 512 * 20, readable.Length);

                await output.FlushAsync();
            }
        }


        [Fact]
        public void PrimitiveReverseWorksCorrectly()
        {
            Assert.Equal((ulong)0x1122334455667788, DefaultWritableBufferExtensions.Reverse((ulong)0x8877665544332211));
            Assert.Equal((long)0x1122334455667788, DefaultWritableBufferExtensions.Reverse(unchecked((long)0x8877665544332211)));
            Assert.Equal((uint)0x11223344, DefaultWritableBufferExtensions.Reverse((uint)0x44332211));
            Assert.Equal((int)0x11223344, DefaultWritableBufferExtensions.Reverse((int)0x44332211));
            Assert.Equal((ushort)0x1122, DefaultWritableBufferExtensions.Reverse((ushort)0x2211));
            Assert.Equal((short)0x1122, DefaultWritableBufferExtensions.Reverse((short)0x2211));
            Assert.Equal((byte)0x11, DefaultWritableBufferExtensions.Reverse((byte)0x11));
            Assert.Equal((sbyte)0x11, DefaultWritableBufferExtensions.Reverse((sbyte)0x11));
        }

        [Fact]
        public unsafe void SpanEndianReadWorksCorrectly()
        {
            Assert.True(BitConverter.IsLittleEndian);

            ulong value = 0x8877665544332211; // [11 22 33 44 55 66 77 88]
            var span = new Span<byte>(&value, 8);

            Assert.Equal<byte>(0x11, span.ReadBigEndian<byte>());
            Assert.Equal<byte>(0x11, span.ReadLittleEndian<byte>());
            Assert.Equal<sbyte>(0x11, span.ReadBigEndian<sbyte>());
            Assert.Equal<sbyte>(0x11, span.ReadLittleEndian<sbyte>());

            Assert.Equal<ushort>(0x1122, span.ReadBigEndian<ushort>());
            Assert.Equal<ushort>(0x2211, span.ReadLittleEndian<ushort>());
            Assert.Equal<short>(0x1122, span.ReadBigEndian<short>());
            Assert.Equal<short>(0x2211, span.ReadLittleEndian<short>());

            Assert.Equal<uint>(0x11223344, span.ReadBigEndian<uint>());
            Assert.Equal<uint>(0x44332211, span.ReadLittleEndian<uint>());
            Assert.Equal<int>(0x11223344, span.ReadBigEndian<int>());
            Assert.Equal<int>(0x44332211, span.ReadLittleEndian<int>());

            Assert.Equal<ulong>(0x1122334455667788, span.ReadBigEndian<ulong>());
            Assert.Equal<ulong>(0x8877665544332211, span.ReadLittleEndian<ulong>());
            Assert.Equal<long>(0x1122334455667788, span.ReadBigEndian<long>());
            Assert.Equal<long>(unchecked((long)0x8877665544332211), span.ReadLittleEndian<long>());
        }
    }
}
