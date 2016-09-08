using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class WritableBufferFacts
    {
        [Fact]
        public async Task CanWriteNothingToBuffer()
        {
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);
                var buffer = channel.Alloc();
                buffer.CommitBytes(0); // doing nothing, the hard way
                await buffer.FlushAsync();
            }
        }
    }
}
