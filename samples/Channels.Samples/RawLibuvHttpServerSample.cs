using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Formatting;
using System.Threading.Tasks;
using Channels.Networking.Libuv;
using Channels.Samples.Formatting;
using Channels.Text.Primitives;

namespace Channels.Samples
{
    public class RawLibuvHttpServerSample
    {
        public static void Run()
        {
            // This sample makes some assumptions

            var ip = IPAddress.Any;
            int port = 5000;
            var thread = new UvThread();
            var listener = new UvTcpListener(thread, new IPEndPoint(ip, port));
            listener.OnConnection(async connection =>
            {
                // Wait for data
                var input = await connection.Input.ReadAsync();

                if (input.IsEmpty && connection.Input.Completion.IsCompleted)
                {
                    // No more data
                    return;
                }

                // Dump the request
                Console.WriteLine(input.GetAsciiString());

                var formatter = connection.Output.GetFormatter(EncodingData.InvariantUtf8);

                unsafe
                {
                    formatter.Append("HTTP/1.1 200 OK");
                    formatter.Append("\r\nConnection: close");
                    formatter.Append("\r\n\r\n");
                    formatter.Append("Hello World!");
                }

                await formatter.Buffer.FlushAsync();

                // Consume the input
                input.Consumed();

                // Close the input channel, which will tell the producer to stop producing
                connection.Input.CompleteReading();

                // Close the output channel, which will close the connection
                connection.Output.CompleteWriting();
            });

            listener.Start();

            Console.WriteLine($"Listening on {ip} on port {port}");
            Console.ReadKey();

            listener.Stop();
            thread.Dispose();
        }
    }
}
