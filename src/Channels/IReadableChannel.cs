using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Channels
{
    public interface IReadableChannel : ICriticalNotifyCompletion
    {
        // Make it awaitable
        bool IsCompleted { get; }
        void GetResult();
        IReadableChannel GetAwaiter();

        Task Completion { get; }

        MemoryPoolSpan BeginRead();
        void EndRead(MemoryPoolIterator consumed, MemoryPoolIterator examined);

        void CompleteReading();
    }
}
