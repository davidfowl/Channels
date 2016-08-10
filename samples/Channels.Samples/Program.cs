using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Channels.Samples.IO.Compression;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            using (var pool = new MemoryPool())
            {
                var filePath = Path.GetFullPath("Program.cs");

                var fs = File.OpenRead(filePath);
                var compressed = new MemoryStream();
                var compressStream = new DeflateStream(compressed, CompressionMode.Compress);
                fs.CopyTo(compressStream);
                compressStream.Flush();
                compressed.Seek(0, SeekOrigin.Begin);

                var channelFactory = new ChannelFactory(pool);
                var input = channelFactory.CreateReadableChannel(compressed);
                // input = new HexChannel(input, pool);

                input = new ReadableDeflateChannel(input, pool);

                // Wrap the console in a writable channel
                var output = channelFactory.CreateWritableChannel(Console.OpenStandardOutput());

                // Copy from the file channel to the console channel
                input.CopyToAsync(output).GetAwaiter().GetResult();

                input.CompleteReading();

                output.CompleteWriting();

                Console.ReadLine();
            }
        }

        private static void Dump(byte[] v)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                builder.Append(v[i].ToString("X2"));
                builder.Append(" ");
            }
            builder.AppendLine();
            Console.WriteLine(builder);
        }
    }
}
