using System;
using System.IO;
using System.Net;
using Channels.Networking.Libuv;
using Channels.Text.Primitives;

namespace Channels.Samples.Framing
{
    public static class ProtocolHandling
    {
        public static void Run()
        {
            var ip = IPAddress.Any;
            int port = 5000;
            var thread = new UvThread();
            var listener = new UvTcpListener(thread, new IPEndPoint(ip, port));
            listener.OnConnection(async connection =>
            {
                var channel = MakePipeline(connection);

                var decoder = new LineDecoder();

                try
                {
                    while (true)
                    {
                        // Wait for data
                        var input = await connection.Input.ReadAsync();

                        try
                        {
                            if (input.IsEmpty && connection.Input.Reading.IsCompleted)
                            {
                                // No more data
                                break;
                            }

                            string line;
                            if (!decoder.TryDecode(ref input, out line))
                            {
                                if (connection.Input.Reading.IsCompleted)
                                {
                                    // Didn't get the whole frame and the connection ended
                                    throw new EndOfStreamException();
                                }

                                // Need more data
                                continue;
                            }

                            Console.WriteLine(line);

                        }
                        finally
                        {
                            // Consume the input
                            connection.Input.Advance(input.Start, input.End);
                        }
                    }
                }
                finally
                {
                    // Close the input channel, which will tell the producer to stop producing
                    connection.Input.Complete();

                    // Close the output channel, which will close the connection
                    connection.Output.Complete();
                }
            });

            listener.Start();

            Console.WriteLine($"Listening on {ip} on port {port}");
            Console.ReadKey();

            listener.Stop();
            thread.Dispose();
        }

        public static IChannel MakePipeline(IChannel channel)
        {
            // Do something fancy here to wrap the channel, SSL etc
            return channel;
        }
    }

    public class LineDecoder : IFrameDecoder<string>
    {
        public bool TryDecode(ref ReadableBuffer input, out string frame)
        {
            ReadableBuffer slice;
            ReadCursor cursor;
            if (!input.TrySliceTo((byte)'\r', (byte)'\n', out slice, out cursor))
            {
                frame = slice.GetUtf8String();
                input = input.Slice(cursor).Slice(2);
                return true;
            }

            frame = null;
            return false;
        }
    }

    public interface IFrameDecoder<TInput>
    {
        bool TryDecode(ref ReadableBuffer input, out TInput frame);
    }
}
