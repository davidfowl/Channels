using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public class ChannelFactory : IDisposable
    {
        private readonly MemoryPool _pool;

        public ChannelFactory() : this(new MemoryPool())
        {
        }

        public ChannelFactory(MemoryPool pool)
        {
            _pool = pool;
        }

        public MemoryPoolChannel CreateChannel() => new MemoryPoolChannel(_pool);

        public IReadableChannel MakeReadableChannel(Stream stream)
        {
            if (!stream.CanRead)
            {
                throw new InvalidOperationException();
            }

            var channel = new MemoryPoolChannel(_pool);

            channel.OnStartReading(() =>
            {
                stream.CopyToAsync(channel).ContinueWith(task =>
                {
                });
            });

            return channel;
        }

        public IWritableChannel MakeWriteableChannel(Stream stream)
        {
            if (!stream.CanWrite)
            {
                throw new InvalidOperationException();
            }

            var channel = new MemoryPoolChannel(_pool);

            channel.CopyToAsync(stream).ContinueWith((task) =>
            {
            });

            return channel;
        }

        public IWritableChannel MakeWriteableChannel(IWritableChannel channel, Func<IReadableChannel, IWritableChannel, Task> consume)
        {
            var newChannel = new MemoryPoolChannel(_pool);

            consume(newChannel, channel).ContinueWith(t =>
            {

            });

            return newChannel;
        }

        public IReadableChannel MakeReadableChannel(IReadableChannel channel, Func<IReadableChannel, IWritableChannel, Task> produce)
        {
            var newChannel = new MemoryPoolChannel(_pool);

            // TODO: Avoid closure
            newChannel.OnStartReading(() =>
            {
                produce(channel, newChannel).ContinueWith(t =>
                {

                });
            });

            return newChannel;
        }

        public void Dispose() => _pool.Dispose();
    }
}
