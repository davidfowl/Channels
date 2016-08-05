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

            var input = new ReadableFileChannel(pool);
            input.OpenReadFile(file);

            // Write data to the channel
            //var data = Encoding.UTF8.GetBytes("Hello World\r\n");
            //channel.WriteAsync(data, 0, data.Length);

            // Wrap it in another channel
            // var other = new UpperCaseChannel(channel, pool);

            var channelFactory = new ChannelFactory(pool);
            // Wrap the console in a writable channel
            var output = channelFactory.CreateWritableChannel(Console.OpenStandardOutput());

            // Copy from the file channel to the console channel
            input.CopyToAsync(output).GetAwaiter().GetResult();
        }
    }
}
