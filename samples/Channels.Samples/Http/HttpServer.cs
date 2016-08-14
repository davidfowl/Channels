using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace Channels.Samples.Http
{
    public class HttpServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        private Socket _listenSocket;

        public HttpServer()
        {
            Features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
        }

        public async void Start<TContext>(IHttpApplication<TContext> application)
        {
            var feature = Features.Get<IServerAddressesFeature>();
            var address = feature.Addresses.FirstOrDefault();
            var uri = new Uri(address);

            _listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var ip = string.Equals(uri.Host, "localhost") ? IPAddress.Loopback : IPAddress.Parse(uri.Host);
            _listenSocket.Bind(new IPEndPoint(ip, uri.Port));
            _listenSocket.Listen(10);

            using (var pool = new MemoryPool())
            {
                while (true)
                {
                    var clientSocket = await _listenSocket.AcceptAsync();

                    try
                    {
                        var task = ProcessClient(application, pool, clientSocket);
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }
        }

        public void Dispose()
        {
            _listenSocket?.Dispose();
        }

        private static async Task ProcessClient<TContext>(IHttpApplication<TContext> application, MemoryPool pool, Socket socket)
        {
            using (var ns = new NetworkStream(socket))
            {
                var id = Guid.NewGuid();
                var channelFactory = new ChannelFactory(pool);
                var input = channelFactory.MakeReadableChannel(ns);
                var output = channelFactory.MakeWriteableChannel(ns);
                var connection = new HttpConnection<TContext>();
                // output = channelFactory.MakeWriteableChannel(output, Dump);
                output = channelFactory.MakeWriteableChannel(output, connection.ProcessResponseBody);
                // input = channelFactory.MakeReadableChannel(input, Dump);

                connection.Input = input;
                connection.Output = output;
                connection.Application = application;

                Console.WriteLine($"[{id}]: Connection started");

                while (true)
                {
                    await connection.ProcessRequest();

                    if (input.Completion.IsCompleted)
                    {
                        break;
                    }

                    if (!connection.KeepAlive)
                    {
                        break;
                    }
                }

                Console.WriteLine($"[{id}]: Connection ended");

                output.CompleteWriting();

                input.CompleteReading();
            }
        }

        private static async Task Dump(IReadableChannel input, IWritableChannel output)
        {
            await input.CopyToAsync(output, span =>
            {
                Console.Write(Encoding.UTF8.GetString(span.Buffer.Array, span.Buffer.Offset, span.Length));
            });

            input.CompleteReading();
            output.CompleteWriting();
        }
    }
}
