using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Channels.Samples.Framing;
using Channels.Text.Primitives;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var channel = new UnpooledChannel();
            var c = Consume(channel);
            var p = Produce(channel);

            Task.WaitAll(c, p);
            // AspNetHttpServerSample.Run();
            // RawLibuvHttpServerSample.Run();
            // ProtocolHandling.Run();
        }

        private static async Task Consume(IReadableChannel channel)
        {
            var buffers = new List<ReadableBuffer>();


            while (true)
            {
                var buffer = await channel.ReadAsync();

                try
                {
                    if (buffer.IsEmpty && channel.Reading.IsCompleted)
                    {
                        break;
                    }

                    buffers.Add(buffer.Preserve());
                }
                finally
                {
                    channel.Advance(buffer.End);
                }
            }

            foreach (var b in buffers)
            {
                Console.WriteLine(b.GetUtf8String());
                b.Dispose();
            }
        }

        private static async Task Produce(UnpooledChannel c)
        {
            var buffer = new byte[100];
            for (int i = 0; i < 10; i++)
            {
                var s = i.ToString();
                int count = Encoding.UTF8.GetBytes(s, 0, s.Length, buffer, 0);
                await c.WriteAsync(new ArraySegment<byte>(buffer, 0, count), CancellationToken.None);
                await Task.Delay(100);
            }

            c.CompleteWriter();
        }
    }
}
