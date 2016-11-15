using System;
using System.Runtime.CompilerServices;

namespace Channels
{
    /// <summary>
    /// An awaitable object that represents an asynchronous read operation
    /// </summary>
    public struct ReadableChannelAwaitable : ICriticalNotifyCompletion
    {
        private readonly IReadableBufferAwaiter _awaiter;

        public ReadableChannelAwaitable(IReadableBufferAwaiter awaiter)
        {
            _awaiter = awaiter;
        }

        public bool IsCompleted => _awaiter.IsCompleted;

        public ChannelReadResult GetResult() => _awaiter.GetResult();

        public ReadableChannelAwaitable GetAwaiter() => this;

        public void UnsafeOnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);

        public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);
    }
}
