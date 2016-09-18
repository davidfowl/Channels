using System;
using System.IO;
using System.IO.Compression;
using Channels.Samples.IO.Compression;

namespace Channels.Samples
{
    public class CompressionSample
    {
        public static void Run()
        {
            using (var channelFactory = new ChannelFactory())
            {
                var filePath = Path.GetFullPath("Program.cs");

                // This is what Stream looks like
                //var fs = File.OpenRead(filePath);
                //var compressed = new MemoryStream();
                //var compressStream = new DeflateStream(compressed, CompressionMode.Compress);
                //fs.CopyTo(compressStream);
                //compressStream.Flush();
                //compressed.Seek(0, SeekOrigin.Begin);
                // var input = channelFactory.MakeReadableChannel(compressed);

                var fs = File.OpenRead(filePath);
                var input = channelFactory.MakeReadableChannel(fs);
                input = channelFactory.CreateDeflateCompressChannel(input, CompressionLevel.Optimal);

                input = channelFactory.CreateDeflateDecompressChannel(input);

                // Wrap the console in a writable channel
                var output = channelFactory.MakeWriteableChannel(Console.OpenStandardOutput());

                // Copy from the file channel to the console channel
                input.CopyToAsync(output).GetAwaiter().GetResult();

                input.Complete();

                output.Complete();
            }
        }
    }
}
