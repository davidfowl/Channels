using System;

namespace Channels
{
    public interface IReadableBufferAwaiter
    {
        bool IsCompleted { get; }

        ReadResult GetResult();

        void OnCompleted(Action continuation);
    }
}
