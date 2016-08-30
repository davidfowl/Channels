using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Channels
{
    public interface IReadableChannel : ICriticalNotifyCompletion
    {
        // Make it awaitable
        bool IsCompleted { get; }
        ReadableBuffer GetResult();
        IReadableChannel GetAwaiter();

        Task Completion { get; }

        void CompleteReading(Exception exception = null);
    }
}
