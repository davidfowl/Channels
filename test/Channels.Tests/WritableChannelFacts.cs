using System;
using System.Text;
using System.Threading.Tasks;
using Channels.Text.Primitives;
using Xunit;

namespace Channels.Tests
{
    public class WritableChannelFacts
    {
        [Fact]
        public async Task CanWriteNothingToBuffer()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();
                var buffer = channel.Alloc();

                Assert.True(buffer.Memory.IsEmpty);

                buffer.Advance(0); // doing nothing, the hard way

                Assert.True(buffer.Memory.IsEmpty);

                await buffer.FlushAsync();

                Assert.True(buffer.Memory.IsEmpty);
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
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();
                var buffer = channel.Alloc();

                Assert.True(buffer.Memory.IsEmpty);

                buffer.WriteUInt64(value);

                Assert.False(buffer.Memory.IsEmpty);

                await buffer.FlushAsync();

                Assert.True(buffer.Memory.IsEmpty);
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
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();

                var output = channel.Alloc();
                Assert.True(output.Memory.IsEmpty);

                output.Write(data);

                Assert.False(output.Memory.IsEmpty);

                await output.FlushAsync();

                Assert.True(output.Memory.IsEmpty);

                channel.Complete();

                Assert.True(output.Memory.IsEmpty);
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
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();
                var output = channel.Alloc();

                Assert.True(output.Memory.IsEmpty);

                output.WriteUtf8String(data);

                Assert.False(output.Memory.IsEmpty);

                await output.FlushAsync();

                Assert.True(output.Memory.IsEmpty);

                channel.Complete();

                Assert.True(output.Memory.IsEmpty);

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
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();

                var output = channel.Alloc();

                Assert.True(output.Memory.IsEmpty);

                output.WriteAsciiString(data);

                Assert.False(output.Memory.IsEmpty);

                await output.FlushAsync();

                Assert.True(output.Memory.IsEmpty);

                channel.Complete();
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
        public void CanReReadDataThatHasNotBeenCommitted_SmallData()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();
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
        public void CanReReadDataThatHasNotBeenCommitted_LargeData()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();

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
                foreach (var memory in readable)
                {
                    var span = memory.Span;
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
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();

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
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateNullChannel();

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
    }
}
