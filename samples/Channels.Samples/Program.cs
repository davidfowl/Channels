using System;
using System.IO;
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
            var file = Path.GetFullPath("Program.cs");

            var channel = new ReadableFileChannel(pool);
            channel.OpenReadFile(file);

            // Write data to the channel
            //var data = Encoding.UTF8.GetBytes("Hello World\r\n");
            //channel.WriteAsync(data, 0, data.Length);

            // Wrap it in another channel
            // var other = new UpperCaseChannel(channel, pool);

            // Consume the data
            ReadLines(channel).GetAwaiter().GetResult();
        }

        private static async Task ReadLines(IReadableChannel channel)
        {
            // Using SIMD to scan through bytes quickly
            var newLine = new Vector<byte>((byte)'\n');

            while (true)
            {
                await channel;

                var end = channel.BeginRead().Begin;
                var start = end;

                // If we're at the end of the channel then stop
                if (end.IsEnd && channel.Completion.IsCompleted)
                {
                    break;
                }

                try
                {
                    while (end.Seek(ref newLine) != -1)
                    {
                        // Get the data from the start to where we found the \n
                        var line = start.GetArraySegment(end);

                        Console.WriteLine(Encoding.UTF8.GetString(line.Array, line.Offset, line.Count));
                        // Skip /n
                        end.Skip(1);
                        start = end;
                    }
                }
                finally
                {
                    channel.EndRead(end);
                }
            }

            // Tell the channel we're done reading
            channel.CompleteReading();
        }
    }
}
