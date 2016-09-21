using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Formatting;
using System.Threading.Tasks;
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
                var handler = new LineHandler()
                {
                    Channel = channel
                };

                try
                {
                    while (true)
                    {
                        // Wait for data
                        var input = await channel.Input.ReadAsync();

                        try
                        {
                            if (input.IsEmpty && channel.Input.Reading.IsCompleted)
                            {
                                // No more data
                                break;
                            }

                            Line line;
                            if (!decoder.TryDecode(ref input, out line))
                            {
                                if (channel.Input.Reading.IsCompleted)
                                {
                                    // Didn't get the whole frame and the connection ended
                                    throw new EndOfStreamException();
                                }

                                // Need more data
                                continue;
                            }

                            await handler.HandleAsync(line);

                        }
                        finally
                        {
                            // Consume the input
                            channel.Input.Advance(input.Start, input.End);
                        }
                    }
                }
                finally
                {
                    // Close the input channel, which will tell the producer to stop producing
                    channel.Input.Complete();

                    // Close the output channel, which will close the connection
                    channel.Output.Complete();
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

    public class Line
    {
        public string Data { get; set; }
    }

    public class LineHandler : IFrameHandler<Line>
    {
        private IChannel _channel;

        public IChannel Channel
        {
            get
            {
                return _channel;
            }
            set
            {
                _channel = value;
                Formatter = new WritableChannelFormatter(value.Output, EncodingData.InvariantUtf8);
            }
        }

        public WritableChannelFormatter Formatter { get; private set; }

        public Task HandleAsync(Line message)
        {
            Formatter.Append(message.Data);
            return Formatter.FlushAsync();
        }
    }

    public class LineDecoder : IFrameDecoder<Line>
    {
        public bool TryDecode(ref ReadableBuffer input, out Line frame)
        {
            ReadableBuffer slice;
            ReadCursor cursor;
            if (input.TrySliceTo((byte)'\r', (byte)'\n', out slice, out cursor))
            {
                frame = new Line { Data = slice.GetUtf8String() };
                input = input.Slice(cursor).Slice(1);
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

    public interface IFrameOutput
    {
        Task WriteAsync(object value);
    }

    public interface IFrameHandler<TInput>
    {
        IChannel Channel { get; set; }

        Task HandleAsync(TInput message);
    }
}
