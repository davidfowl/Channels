using System.Threading.Tasks;
using Channels.Text.Primitives;
using Xunit;
using System;

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
                WritableBufferExtensions.WriteUInt64(ref buffer, value);
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
                channel.CompleteWriting();

                int offset = 0;
                while(true)
                {
                    var input = await channel.ReadAsync();
                    if (input.Length == 0) break;

                    Assert.True(input.Equals(new Span<byte>(data, offset, input.Length)));
                    offset += input.Length;
                    input.Consumed();
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
                WritableBufferExtensions.WriteUtf8String(ref output, data);
                var foo = output.Memory.IsEmpty; // trying to see if .Memory breaks
                await output.FlushAsync();
                channel.CompleteWriting();

                int offset = 0;
                while (true)
                {
                    var input = await channel.ReadAsync();
                    if (input.Length == 0) break;

                    string s = ReadableBufferExtensions.GetUtf8String(input);
                    Assert.Equal(data.Substring(offset, input.Length), s);
                    offset += input.Length;
                    input.Consumed();
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
                WritableBufferExtensions.WriteAsciiString(ref output, data);
                var foo = output.Memory.IsEmpty; // trying to see if .Memory breaks
                await output.FlushAsync();
                channel.CompleteWriting();

                int offset = 0;
                while (true)
                {
                    var input = await channel.ReadAsync();
                    if (input.Length == 0) break;

                    string s = ReadableBufferExtensions.GetAsciiString(input);
                    Assert.Equal(data.Substring(offset, input.Length), s);
                    offset += input.Length;
                    input.Consumed();
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
    }
}
