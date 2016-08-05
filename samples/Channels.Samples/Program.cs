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
            using (var pool = new MemoryPool())
            {
                var filePath = Path.GetFullPath("Program.cs");

                // Open the file 
                var input = new ReadableFileChannel(pool);
                input.OpenReadFile(filePath);

                var channelFactory = new ChannelFactory(pool);
                // Wrap the console in a writable channel
                var output = channelFactory.CreateWritableChannel(Console.OpenStandardOutput());

                // Copy from the file channel to the console channel
                input.CopyToAsync(output).GetAwaiter().GetResult();

                input.CompleteReading();

                output.CompleteWriting();

                Console.ReadLine();
            }
        }
    }
}
