using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public abstract class ReadableChannel : IReadableChannel
    {
        protected Channel _channel;

        public ReadableChannel(MemoryPool pool)
        {
            _channel = new Channel(pool);
        }

        public Task Completion => _channel.Input.Completion;

        public void CompleteReading(Exception error = null) => _channel.Input.CompleteReading(error);

        public ChannelAwaitable ReadAsync() => _channel.Input.ReadAsync();
    }
}
