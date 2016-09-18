using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Formatting;
using Channels.Networking.Libuv;
using Channels.Text.Primitives;

namespace Channels.Samples
{
    public class RawLibuvHttpServerSample
    {
        public static void Run()
        {
            var ip = IPAddress.Any;
            int port = 5000;
            var thread = new UvThread();
            var listener = new UvTcpListener(thread, new IPEndPoint(ip, port));
            listener.OnConnection(async connection =>
            {
                var httpParser = new HttpRequestParser();
                var formatter = connection.Output.GetFormatter(EncodingData.InvariantUtf8);

                try
                {
                    while (true)
                    {
                        httpParser.Reset();

                        // Wait for data
                        var input = await connection.Input.ReadAsync();

                        try
                        {
                            if (input.IsEmpty && connection.Input.Reading.IsCompleted)
                            {
                                // No more data
                                break;
                            }

                            // Parse the input http request
                            var result = httpParser.ParseRequest(ref input);

                            switch (result)
                            {
                                case HttpRequestParser.ParseResult.Incomplete:
                                    if (connection.Input.Reading.IsCompleted)
                                    {
                                        // Didn't get the whole request and the connection ended
                                        throw new Exception();
                                    }
                                    // Need more data
                                    continue;
                                case HttpRequestParser.ParseResult.Complete:
                                    break;
                                case HttpRequestParser.ParseResult.BadRequest:
                                    throw new Exception();
                                default:
                                    break;
                            }

                            unsafe
                            {
                                formatter.Append("HTTP/1.1 200 OK");
                                formatter.Append("\r\nContent-Length: 13");
                                formatter.Append("\r\nContent-Type: text/plain");
                                formatter.Append("\r\n\r\n");
                                formatter.Append("Hello, World!");
                            }

                            await formatter.FlushAsync();

                        }
                        finally
                        {
                            // Consume the input
                            input.Consumed(input.Start, input.End);
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
    }
}
