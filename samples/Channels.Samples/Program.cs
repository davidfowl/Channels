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
            var fs = File.OpenRead("Program.cs");

            var channelFactory = new ChannelFactory();
            var channel = channelFactory.CreateReadableChannel(fs);

            ReadLines(channel).GetAwaiter().GetResult();

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
