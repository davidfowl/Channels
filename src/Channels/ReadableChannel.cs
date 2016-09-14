using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public abstract class ReadableChannel : IReadableChannel
    {
        protected Channel _channel;

        public ReadableChannel(IBufferPool pool)
        {
            _channel = new Channel(pool);
        }

        public Task Completion => ((IReadableChannel)_channel).Completion;

        public void CompleteReading(Exception error = null) => _channel.CompleteReading(error);

        public ChannelAwaitable ReadAsync() => _channel.ReadAsync();
    }
}
