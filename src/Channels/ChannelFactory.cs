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

        public IReadableChannel CreateReadableChannel(Stream stream)
        {
            if (!stream.CanRead)
            {
                throw new InvalidOperationException();
            }

            var channel = new MemoryPoolChannel(_pool);

            channel.OnStartReading(() =>
            {
                stream.CopyToAsync(channel).ContinueWith((task, state) =>
                {
                    ((Stream)state).Dispose();
                },
                stream);
            });

            return channel;
        }

        public IWritableChannel CreateWritableChannel(Stream stream)
        {
            if (!stream.CanWrite)
            {
                throw new InvalidOperationException();
            }

            var channel = new MemoryPoolChannel(_pool);

            channel.CopyToAsync(stream).ContinueWith((task, state) =>
            {
                ((Stream)state).Dispose();
            },
            stream);

            return channel;
        }

        public void Dispose() => _pool.Dispose();
    }
}
