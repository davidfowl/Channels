using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class ChannelFacts
    {
        [Fact]
        public async Task WritingDataMakesDataReadableViaChannel()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                var bytes = Encoding.ASCII.GetBytes("Hello World");

                await channel.WriteAsync(bytes.Slice());
                var buffer = await channel.ReadAsync();

                Assert.Equal(11, buffer.Length);
                Assert.True(buffer.IsSingleSpan);
                var array = new byte[11];
                buffer.FirstSpan.TryCopyTo(array);
                Assert.Equal("Hello World", Encoding.ASCII.GetString(array));
            }
        }

        [Fact]
        public async Task HelloWorldAcrossTwoBlocks()
        {
            const int blockSize = 4032;
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                //     block 1       ->    block2
                // [padding..hello]  ->  [  world   ]
                var paddingBytes = Enumerable.Repeat((byte)'a', blockSize - 5).ToArray();
                var bytes = Encoding.ASCII.GetBytes("Hello World");
                var writeBuffer = channel.Alloc();
                writeBuffer.Write(paddingBytes.Slice());
                writeBuffer.Write(bytes.Slice());
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                Assert.False(buffer.IsSingleSpan);
                var helloBuffer = buffer.Slice(blockSize - 5);
                Assert.False(helloBuffer.IsSingleSpan);
                var spans = helloBuffer.AsEnumerable().ToList();
                Assert.Equal(2, spans.Count);
                var helloBytes = new byte[spans[0].Length];
                spans[0].TryCopyTo(helloBytes);
                var worldBytes = new byte[spans[1].Length];
                spans[1].TryCopyTo(worldBytes);
                Assert.Equal("Hello", Encoding.ASCII.GetString(helloBytes));
                Assert.Equal(" World", Encoding.ASCII.GetString(worldBytes));
            }
        }

        [Fact]
        public async Task IndexOfNotFoundReturnsEnd()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                var bytes = Encoding.ASCII.GetBytes("Hello World");

                await channel.WriteAsync(bytes.Slice());
                var buffer = await channel.ReadAsync();
                var delim = buffer.IndexOf(ref CommonVectors.LF);

                Assert.True(delim == ReadCursor.NotFound);
            }
        }

        [Fact]
        public async Task FastPathIndexOfAcrossBlocks()
        {
            var vecUpperR = new Vector<byte>((byte)'R');

            const int blockSize = 4032;
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                //     block 1       ->    block2
                // [padding..hello]  ->  [  world   ]
                var paddingBytes = Enumerable.Repeat((byte)'a', blockSize - 5).ToArray();
                var bytes = Encoding.ASCII.GetBytes("Hello World");
                var writeBuffer = channel.Alloc();
                writeBuffer.Write(paddingBytes.Slice());
                writeBuffer.Write(bytes.Slice());
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                var delim = buffer.IndexOf(ref vecUpperR);
                Assert.True(delim == ReadCursor.NotFound);
            }
        }

        [Fact]
        public async Task SlowPathIndexOfAcrossBlocks()
        {
            const int blockSize = 4032;
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                //     block 1       ->    block2
                // [padding..hello]  ->  [  world   ]
                var paddingBytes = Enumerable.Repeat((byte)'a', blockSize - 5).ToArray();
                var bytes = Encoding.ASCII.GetBytes("Hello World");
                var writeBuffer = channel.Alloc();
                writeBuffer.Write(paddingBytes.Slice());
                writeBuffer.Write(bytes.Slice());
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                var delim = buffer.IndexOf(ref CommonVectors.Space);
                Assert.False(buffer.IsSingleSpan);
                Assert.False(delim == ReadCursor.NotFound);

                var slice = buffer.Slice(delim).Slice(1);
                var array = slice.ToArray();

                Assert.Equal("World", Encoding.ASCII.GetString(array));
            }
        }
    }
}
