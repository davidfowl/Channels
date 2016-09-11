using System;
using System.Runtime.CompilerServices;

namespace Channels
{
    public struct ChannelAwaitable : ICriticalNotifyCompletion
    {
        private readonly Channel _channel;

        internal ChannelAwaitable(Channel channel)
        {
            _channel = channel;
        }

        public bool IsCompleted => _channel.IsCompleted;

        public ReadableBuffer GetResult() => _channel.GetResult();

        public ChannelAwaitable GetAwaiter() => this;

        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        public void OnCompleted(Action continuation) => _channel.OnCompleted(continuation);
    }
}
