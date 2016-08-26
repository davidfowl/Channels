using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Formatting;
using System.Threading;
using System.Threading.Tasks;
using Channels;
using Channels.Samples.Formatting;
using Channels.Samples.Http;
using Channels.Samples.IO;
using Channels.Samples.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // RunRawLibuvHttpServer();
            RunAspNetHttpServer();
            // RunCompressionSample();
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception);
        }

        private static void RunCompressionSample()
        {
            using (var pool = new MemoryPool())
            {
                var channelFactory = new ChannelFactory(pool);

                var filePath = Path.GetFullPath("Program.cs");

                //var fs = File.OpenRead(filePath);
                //var compressed = new MemoryStream();
                //var compressStream = new DeflateStream(compressed, CompressionMode.Compress);
                //fs.CopyTo(compressStream);
                //compressStream.Flush();
                //compressed.Seek(0, SeekOrigin.Begin);
                // var input = channelFactory.MakeReadableChannel(compressed);

                var fs1 = File.OpenRead(filePath);
                var input = channelFactory.MakeReadableChannel(fs1);
                input = channelFactory.CreateDeflateCompressChannel(input, CompressionLevel.Optimal);

                input = channelFactory.CreateDeflateDecompressChannel(input);

                // Wrap the console in a writable channel
                var output = channelFactory.MakeWriteableChannel(Console.OpenStandardOutput());

                // Copy from the file channel to the console channel
                input.CopyToAsync(output).GetAwaiter().GetResult();

                input.CompleteReading();

                output.CompleteWriting();

                Console.ReadLine();
            }
        }

        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

        private static void RunRawLibuvHttpServer()
        {
            // This sample makes some assumptions

            var ip = IPAddress.Any;
            int port = 5000;
            var listener = new UvTcpListener(ip, port);
            listener.OnConnection(async connection =>
            {
                // Wait for data
                await connection.Input;

                // Get the buffer
                var input = connection.Input.BeginRead();

                if (input.IsEmpty && connection.Input.Completion.IsCompleted)
                {
                    // No more data
                    return;
                }

                // Dump the request
                Console.WriteLine(input.GetAsciiString());

                var formatter = connection.Output.GetFormatter(FormattingData.InvariantUtf8);

                unsafe
                {
                    formatter.Append("HTTP/1.1 200 OK");
                    formatter.Append("\r\nConnection: close");
                    formatter.Append("\r\n\r\n");
                    formatter.Append("Hello World!");
                }

                await connection.Output.WriteAsync(formatter.Buffer);

                // Tell the channel the data is consumed
                connection.Input.EndRead(input);

                // Close the output channel, which will close the connection
                connection.Output.CompleteWriting();
            });

            listener.Start();

            Console.WriteLine($"Listening on {ip} on port {port}");
            Console.ReadKey();

            listener.Stop();
        }

        private static void RunAspNetHttpServer()
        {
            var host = new WebHostBuilder()
                            .UseUrls("http://*:5000")
                            .ConfigureServices(services =>
                            {
                                // Use a custom server
                                services.AddTransient<IServer, HttpServer>();
                            })
                            // .UseKestrel()
                            .Configure(app =>
                            {
                                app.Run(context =>
                                {
                                    context.Response.StatusCode = 200;
                                    context.Response.ContentType = "text/plain";
                                    // HACK: Setting the Content-Length header manually avoids the cost of serializing the int to a string.
                                    //       This is instead of: httpContext.Response.ContentLength = _helloWorldPayload.Length;
                                    context.Response.Headers["Content-Length"] = "13";
                                    return context.Response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldPayload.Length);
                                });
                            })
                            .Build();
            host.Run();
        }
    }
}
