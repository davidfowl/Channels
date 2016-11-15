using System;
using System.Threading.Tasks;

namespace Channels
{
    public abstract class WritableChannel : IWritableChannel
    {
        private readonly Channel _channel;

        public WritableChannel(IBufferPool pool)
        {
            _channel = new Channel(pool);

            Consume(_channel);
        }

        protected abstract Task WriteAsync(ReadableBuffer buffer);

        public Task Writing => _channel.Writing;

        public WritableBuffer Alloc(int minimumSize = 0) => _channel.Alloc(minimumSize);

        public void Complete(Exception exception = null) => _channel.CompleteWriter(exception);

        private async void Consume(IReadableChannel channel)
        {
            while (true)
            {
                var result = await channel.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }

                    await WriteAsync(buffer);
                }
                finally
                {
                    channel.Advance(buffer.End);
                }
            }

            channel.Complete();
        }
    }
}
