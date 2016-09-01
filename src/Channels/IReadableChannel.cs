using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Channels
{
    public interface IReadableChannel
    {
        ChannelAwaitable ReadAsync();

        Task Completion { get; }

        void CompleteReading(Exception exception = null);
    }

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
