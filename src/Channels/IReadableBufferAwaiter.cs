using System;

namespace Channels
{
    public interface IReadableBufferAwaiter
    {
        bool IsCompleted { get; }

        ReadableBuffer GetBuffer();

        void OnCompleted(Action continuation);
    }
}
