using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Factory used to creaet instances of various channels.
    /// </summary>
    public class ChannelFactory : IDisposable
    {
        private readonly IBufferPool _pool;
        private readonly IBufferSegmentFactory _bufferSegmentFactory;

        public ChannelFactory() : this(new MemoryPool())
        {
        }

        public ChannelFactory(IBufferPool pool) : this(pool, new PooledBufferSegmentFactory())
        {

        }

        internal ChannelFactory(IBufferPool pool, IBufferSegmentFactory bufferSegmentFactory)
        {
            _pool = pool;
            _bufferSegmentFactory = bufferSegmentFactory;
        }

        public Channel CreateChannel() => new Channel(_pool, _bufferSegmentFactory);

        public IReadableChannel MakeReadableChannel(Stream stream)
        {
            if (!stream.CanRead)
            {
                throw new InvalidOperationException();
            }

            var channel = new Channel(_pool, _bufferSegmentFactory);
            ExecuteCopyToAsync(channel, stream);
            return channel;
        }

        private async void ExecuteCopyToAsync(Channel channel, Stream stream)
        {
            await channel.ReadingStarted;

            await stream.CopyToAsync(channel);
        }

        public IChannel MakeChannel(Stream stream)
        {
            return new StreamChannel(this, stream);
        }

        public IWritableChannel MakeWriteableChannel(Stream stream)
        {
            if (!stream.CanWrite)
            {
                throw new InvalidOperationException();
            }

            var channel = new Channel(_pool, _bufferSegmentFactory);

            channel.CopyToAsync(stream).ContinueWith((task) =>
            {
                if (task.IsFaulted)
                {
                    channel.CompleteReader(task.Exception);
                }
                else
                {
                    channel.CompleteReader();
                }
            });

            return channel;
        }

        public IWritableChannel MakeWriteableChannel(IWritableChannel channel, Func<IReadableChannel, IWritableChannel, Task> consume)
        {
            var newChannel = new Channel(_pool, _bufferSegmentFactory);

            consume(newChannel, channel).ContinueWith(t =>
            {
            });

            return newChannel;
        }

        public IReadableChannel MakeReadableChannel(IReadableChannel channel, Func<IReadableChannel, IWritableChannel, Task> produce)
        {
            var newChannel = new Channel(_pool, _bufferSegmentFactory);
            Execute(channel, newChannel, produce);
            return newChannel;
        }

        private async void Execute(IReadableChannel channel, Channel newChannel, Func<IReadableChannel, IWritableChannel, Task> produce)
        {
            await newChannel.ReadingStarted;

            await produce(channel, newChannel);
        }

        public void Dispose() => _pool.Dispose();
    }
}
