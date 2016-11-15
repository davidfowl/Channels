using System;

namespace Channels
{
    public interface IReadableBufferAwaiter
    {
        bool IsCompleted { get; }

        ChannelReadResult GetResult();

        void OnCompleted(Action continuation);
    }
}
