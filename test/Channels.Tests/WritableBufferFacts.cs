using System.Threading.Tasks;
using Channels.Text.Primitives;
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

        [Theory]
        [InlineData(1, "1")]
        [InlineData(20, "20")]
        [InlineData(300, "300")]
        [InlineData(4000, "4000")]
        [InlineData(500000, "500000")]
        [InlineData(60000000000000000, "60000000000000000")]
        public async Task CanWriteUInt64ToBuffer(ulong value, string valueAsString)
        {
            using (var memoryPool = new MemoryPool())
            {
                var channel = new Channel(memoryPool);
                var buffer = channel.Alloc();
                WritableBufferExtensions.WriteUInt64(ref buffer, value);
                await buffer.FlushAsync();

                var inputBuffer = await channel.ReadAsync();

                Assert.Equal(valueAsString, inputBuffer.GetUtf8String());
            }
        }
    }
}
