using System;
using System.Runtime.CompilerServices;

namespace Channels
{
    /// <summary>
    /// An awaitable object that represents an asynchronous read operation
    /// </summary>
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

        public void UnsafeOnCompleted(Action continuation) => _channel.UnsafeOnCompleted(continuation);

        public void OnCompleted(Action continuation) => _channel.OnCompleted(continuation);
    }
}
