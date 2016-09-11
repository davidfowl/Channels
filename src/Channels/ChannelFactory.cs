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

        public Channel CreateChannel() => new Channel(_pool);

        public IReadableChannel MakeReadableChannel(Stream stream)
        {
            if (!stream.CanRead)
            {
                throw new InvalidOperationException();
            }

            var channel = new Channel(_pool);
            ExecuteCopyToAsync(channel, stream);
            return channel.Input;
        }

        private async void ExecuteCopyToAsync(Channel channel, Stream stream)
        {
            await channel.ReadingStarted;

            await stream.CopyToAsync(channel.Output);
        }

        public IWritableChannel MakeWriteableChannel(Stream stream)
        {
            if (!stream.CanWrite)
            {
                throw new InvalidOperationException();
            }

            var channel = new Channel(_pool);

            channel.Input.CopyToAsync(stream).ContinueWith((task, state) =>
            {
                var input = (IReadableChannel)state;
                if (task.IsFaulted)
                {
                    input.CompleteReading(task.Exception);
                }
                else
                {
                    input.CompleteReading();
                }
            },
            channel.Input);

            return channel.Output;
        }

        public IWritableChannel MakeWriteableChannel(IWritableChannel channel, Func<IReadableChannel, IWritableChannel, Task> consume)
        {
            var newChannel = new Channel(_pool);

            consume(newChannel.Input, channel).ContinueWith(t =>
            {
            });

            return newChannel.Output;
        }

        public IReadableChannel MakeReadableChannel(IReadableChannel channel, Func<IReadableChannel, IWritableChannel, Task> produce)
        {
            var newChannel = new Channel(_pool);
            Execute(channel, newChannel, produce);
            return newChannel.Input;
        }

        private async void Execute(IReadableChannel channel, Channel newChannel, Func<IReadableChannel, IWritableChannel, Task> produce)
        {
            await newChannel.ReadingStarted;

            await produce(channel, newChannel.Output);
        }

        public void Dispose() => _pool.Dispose();
    }
}
