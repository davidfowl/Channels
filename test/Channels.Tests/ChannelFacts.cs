using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class ChannelFacts
    {
        [Fact]
        public async Task WritingDataMakesDataReadableViaChannel()
        {
            var cf = new ChannelFactory();
            var channel = cf.CreateChannel();
            var bytes = Encoding.ASCII.GetBytes("Hello World");

            await channel.WriteAsync(bytes, 0, bytes.Length);
            var buffer = await channel.ReadAsync();

            Assert.Equal(11, buffer.Length);
            Assert.True(buffer.IsSingleSpan);
            var array = new byte[11];
            buffer.FirstSpan.TryCopyTo(array);
            Assert.Equal("Hello World", Encoding.ASCII.GetString(array));
        }
    }
}
