using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Channels.Samples
{
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

                var fin = inner.Completion.IsCompleted;

                var span = inner.BeginRead();

                if (span.Begin.IsEnd && fin)
                {
                    break;
                }

                // PERF: This might copy
                var data = span.Begin.GetArraySegment(span.End);
                var wi = _channel.BeginWrite();
                for (int i = 0; i < data.Count; i++)
                {
                    byte b = data.Array[data.Offset + i];
                    if (b >= 'a' && b <= 'z')
                    {
                        wi.Write((byte)(b & 0xdf));
                    }
                    else
                    {
                        wi.Write(b);
                    }
                }

                await _channel.EndWriteAsync(wi);

                inner.EndRead(span.End);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var fs = File.OpenRead("Program.cs");

            var pool = new MemoryPool();
            var channelFactory = new ChannelFactory(pool);
            var channel = channelFactory.CreateChannel();

            var iter = channel.BeginWrite();
            iter.CopyFromAscii("Hello World\r\n");
            channel.EndWriteAsync(iter);
            channel.CompleteWriting();

            var other = new UpperCaseChannel(channel, pool);

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

                if (iter.IsEnd && channel.Completion.IsCompleted)
                {
                    break;
                }

                try
                {
                    while (iter.Seek(ref newLine) != -1)
                    {
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

            channel.CompleteReading();
        }
    }
}
