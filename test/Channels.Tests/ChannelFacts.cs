﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Channels.Text.Primitives;
using Xunit;

namespace Channels.Tests
{
    public class ChannelFacts
    {
        [Fact]
        public async Task ReaderShouldNotGetUnflushedBytesWhenOverflowingSegments()
        {
            using (var cf = new ChannelFactory())
            {
                var c = cf.CreateChannel();

                // Fill the block with stuff leaving 5 bytes at the end
                var buffer = c.Alloc(1);

                var len = buffer.Memory.Length;
                // Fill the buffer with garbage
                //     block 1       ->    block2
                // [padding..hello]  ->  [  world   ]
                var paddingBytes = Enumerable.Repeat((byte)'a', len - 5).ToArray();
                buffer.Write(paddingBytes);
                await buffer.FlushAsync();

                // Write 10 and flush
                buffer = c.Alloc();
                buffer.WriteLittleEndian(10);

                // Write 9
                buffer.WriteLittleEndian(9);

                // Write 8
                buffer.WriteLittleEndian(8);

                // Make sure we don't see it yet
                var reader = await c.ReadAsync();

                Assert.Equal(len - 5, reader.Length);

                // Don't move
                c.Advance(reader.End);

                // Now flush
                await buffer.FlushAsync();

                reader = await c.ReadAsync();

                Assert.Equal(12, reader.Length);
                Assert.Equal(10, reader.ReadLittleEndian<int>());
                Assert.Equal(9, reader.Slice(4).ReadLittleEndian<int>());
                Assert.Equal(8, reader.Slice(8).ReadLittleEndian<int>());
            }
        }

        [Fact]
        public async Task ReaderShouldNotGetUnflushedBytes()
        {
            using (var cf = new ChannelFactory())
            {
                var c = cf.CreateChannel();

                // Write 10 and flush
                var buffer = c.Alloc();
                buffer.WriteLittleEndian(10);
                await buffer.FlushAsync();

                // Write 9
                buffer = c.Alloc();
                buffer.WriteLittleEndian(9);

                // Write 8
                buffer.WriteLittleEndian(8);

                // Make sure we don't see it yet
                var reader = await c.ReadAsync();

                Assert.Equal(4, reader.Length);
                Assert.Equal(10, reader.ReadLittleEndian<int>());

                // Don't move
                c.Advance(reader.Start);

                // Now flush
                await buffer.FlushAsync();

                reader = await c.ReadAsync();

                Assert.Equal(12, reader.Length);
                Assert.Equal(10, reader.ReadLittleEndian<int>());
                Assert.Equal(9, reader.Slice(4).ReadLittleEndian<int>());
                Assert.Equal(8, reader.Slice(8).ReadLittleEndian<int>());
            }
        }

        [Fact]
        public async Task ReaderShouldNotGetUnflushedBytesWithAppend()
        {
            using (var cf = new ChannelFactory())
            {
                var c = cf.CreateChannel();

                // Write 10 and flush
                var buffer = c.Alloc();
                buffer.WriteLittleEndian(10);
                await buffer.FlushAsync();

                // Write Hello to another channel and get the buffer
                var bytes = Encoding.ASCII.GetBytes("Hello");

                var c2 = cf.CreateChannel();
                await c2.WriteAsync(bytes);
                var c2Buffer = await c2.ReadAsync();

                Assert.Equal(bytes.Length, c2Buffer.Length);

                // Write 9 to the buffer
                buffer = c.Alloc();
                buffer.WriteLittleEndian(9);

                // Append the data from the other channel
                buffer.Append(ref c2Buffer);

                // Mark it as consumed
                c2.Advance(c2Buffer.End);

                // Now read and make sure we only see the comitted data
                var reader = await c.ReadAsync();

                Assert.Equal(4, reader.Length);
                Assert.Equal(10, reader.ReadLittleEndian<int>());

                // Consume nothing
                c.Advance(reader.Start);

                // Flush the second set of writes
                await buffer.FlushAsync();

                reader = await c.ReadAsync();

                // int, int, "Hello"
                Assert.Equal(13, reader.Length);
                Assert.Equal(10, reader.ReadLittleEndian<int>());
                Assert.Equal(9, reader.Slice(4).ReadLittleEndian<int>());
                Assert.Equal("Hello", reader.Slice(8).GetUtf8String());
            }
        }

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
                buffer.First.Span.TryCopyTo(array);
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
                    channel.CompleteWriter(new OperationCanceledException(cts.Token));
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
                var memory = new List<Memory<byte>>();
                foreach (var m in helloBuffer)
                {
                    memory.Add(m);
                }
                var spans = memory;
                Assert.Equal(2, memory.Count);
                var helloBytes = new byte[spans[0].Length];
                spans[0].Span.TryCopyTo(helloBytes);
                var worldBytes = new byte[spans[1].Length];
                spans[1].Span.TryCopyTo(worldBytes);
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
