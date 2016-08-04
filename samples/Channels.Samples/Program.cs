using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var pool = new MemoryPool();
            var channelFactory = new ChannelFactory(pool);
            var channel = channelFactory.CreateChannel();

            // Write data to the channel
            var data = Encoding.UTF8.GetBytes("Hello World\r\n");
            channel.WriteAsync(data, 0, data.Length);

            // Wrap it in another channel
            var other = new UpperCaseChannel(channel, pool);

            // Consume the data
            ReadLines(other).GetAwaiter().GetResult();
        }

        private static async Task ReadLines(IReadableChannel channel)
        {
            // Using SIMD to scan through bytes quickly
            var newLine = new Vector<byte>((byte)'\n');

            while (true)
            {
                await channel;

                var iter = channel.BeginRead().Begin;
                var start = iter;

                // If we're at the end of the channel then stop
                if (iter.IsEnd && channel.Completion.IsCompleted)
                {
                    break;
                }

                try
                {
                    while (iter.Seek(ref newLine) != -1)
                    {
                        // Get the daata from the start to where we found the \n
                        var line = start.GetArraySegment(iter);

                        Console.WriteLine(Encoding.UTF8.GetString(line.Array, line.Offset, line.Count));
                        // Skip /n
                        iter.Skip(1);
                        start = iter;
                    }
                }
                finally
                {
                    channel.EndRead(iter);
                }
            }

            // Tell te channel we're done consuming
            channel.CompleteReading();
        }
    }

    public class UpperCaseChannel : ReadableChannel
    {
        public UpperCaseChannel(IReadableChannel inner, MemoryPool pool) : base(pool)
        {
            Process(inner);
        }

        private async void Process(IReadableChannel inner)
        {
            while (true)
            {
                await inner;

                var span = inner.BeginRead();

                if (span.Begin.IsEnd && inner.Completion.IsCompleted)
                {
                    break;
                }

                // PERF: This might copy
                var data = span.Begin.GetArraySegment(span.End);
                var iter = _channel.BeginWrite();
                for (int i = 0; i < data.Count; i++)
                {
                    byte b = data.Array[data.Offset + i];
                    if (b >= 'a' && b <= 'z')
                    {
                        // To upper
                        iter.Write((byte)(b & 0xdf));
                    }
                    else
                    {
                        // Leave it alone
                        iter.Write(b);
                    }
                }

                await _channel.EndWriteAsync(iter);

                inner.EndRead(span.End);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();
        }
    }
}
