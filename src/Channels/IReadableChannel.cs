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

        ReadableBuffer BeginRead();
        void EndRead(ReadableBuffer consumed, ReadableBuffer examined);

        void CompleteReading();
    }
}
