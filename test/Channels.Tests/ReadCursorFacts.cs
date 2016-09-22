using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class ReadCursorFacts
    {
        [Fact]
        public async Task RelativeCursorOperatorsWorkAsExpected()
        {
            using (var channelFactory = new ChannelFactory())
            {
                // populate some dummy data
                var channel = channelFactory.CreateChannel();

                var output = channel.Alloc();
                output.Write(new byte[30]);
                output.Ensure(4030);
                output.Write(new byte[30]);
                output.Ensure(4030);
                output.Write(new byte[30]);
                var rb = output.AsReadableBuffer();
                Assert.Equal(3, rb.AsEnumerable().Count()); // forcing multi-span

                // k, the simple things
                var start = rb.Start;

                Assert.Equal(0, rb.Start - rb.Start);
                Assert.Equal(rb.Start, rb.Start + 0);

                var end = rb.End;
                Assert.Equal(0, rb.End - rb.End);
                Assert.Equal(rb.End, rb.End + 0);

                Assert.Equal(90, rb.Length);
                Assert.Equal(90, rb.End - rb.Start);
                Assert.Equal(rb.End, rb.Start + 90);

                Assert.Throws<ArgumentException>(() => rb.Start - rb.End);

                Assert.Throws<ArgumentOutOfRangeException>(() => rb.Start + (-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => rb.End + (-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => rb.End + 1);

                
                for (int i = 0; i <= 90; i++)
                {
                    try
                    {
                        if (i == 30) System.Diagnostics.Debugger.Break();
                        var viaOp = rb.Start + i;
                        var viaSlice = rb.Slice(i).Start;
                        Assert.Equal(viaSlice, viaOp);

                        Assert.Equal(i, viaOp - rb.Start);
                        Assert.Equal(90 - i, rb.End - viaOp);
                    }
                    catch(Exception ex)
                    {
                        throw new Exception($"Failed for {nameof(i)}={i}: {ex.Message}", ex);
                    }
                }


                await output.FlushAsync();
            }
        }
    }
}
