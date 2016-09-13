using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
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

                await channel.WriteAsync(bytes);
                var buffer = await channel.ReadAsync();

                Assert.Equal(11, buffer.Length);
                Assert.True(buffer.IsSingleSpan);
                var array = new byte[11];
                buffer.FirstSpan.TryCopyTo(array);
                Assert.Equal("Hello World", Encoding.ASCII.GetString(array));
            }
        }

        [Fact]
        public async Task ReadingCanBeCancelled()
        {
            using (var cf = new ChannelFactory())
            {
                var channel = cf.CreateChannel();
                var cts = new CancellationTokenSource();
                cts.Token.Register(() =>
                {
                    channel.CompleteWriting(new OperationCanceledException(cts.Token));
                });

                var ignore = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    cts.Cancel();
                });

                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    var buffer = await channel.ReadAsync();
                });
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
                writeBuffer.Write(paddingBytes);
                writeBuffer.Write(bytes);
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

                await channel.WriteAsync(bytes);
                var buffer = await channel.ReadAsync();
                ReadableBuffer slice;
                ReadCursor cursor;

                Assert.False(buffer.TrySliceTo(10, out slice, out cursor));
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
                writeBuffer.Write(paddingBytes);
                writeBuffer.Write(bytes);
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                ReadableBuffer slice;
                ReadCursor cursor;
                Assert.False(buffer.TrySliceTo((byte)'R', out slice, out cursor));
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
                writeBuffer.Write(paddingBytes);
                writeBuffer.Write(bytes);
                await writeBuffer.FlushAsync();

                var buffer = await channel.ReadAsync();
                ReadableBuffer slice;
                ReadCursor cursor;
                Assert.False(buffer.IsSingleSpan);
                Assert.True(buffer.TrySliceTo((byte)' ', out slice, out cursor));

                slice = buffer.Slice(cursor).Slice(1);
                var array = slice.ToArray();

                Assert.Equal("World", Encoding.ASCII.GetString(array));
            }
        }
    }
}
