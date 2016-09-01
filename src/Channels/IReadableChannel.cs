using System;
using System.Threading.Tasks;

namespace Channels
{
    public interface IReadableChannel
    {
        ChannelAwaitable ReadAsync();

        Task Completion { get; }

        void CompleteReading(Exception exception = null);
    }
}
