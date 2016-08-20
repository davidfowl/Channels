using System;
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
            _listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            IPAddress ip;
            int port;
            GetIp(address, out ip, out port);
            _listenSocket.Bind(new IPEndPoint(ip, port));
            _listenSocket.Listen(10);

            using (var pool = new MemoryPool())
            {
                while (true)
                {
                    try
                    {
                        var clientSocket = await _listenSocket.AcceptAsync();
                        clientSocket.NoDelay = true;
                        var task = Task.Factory.StartNew(() => ProcessClient(application, pool, clientSocket));
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
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

        private static void GetIp(string url, out IPAddress ip, out int port)
        {
            ip = null;

            var address = ServerAddress.FromUrl(url);
            switch (address.Host)
            {
                case "localhost":
                    ip = IPAddress.Loopback;
                    break;
                case "*":
                    ip = IPAddress.Any;
                    break;
                default:
                    break;
            }
            ip = ip ?? IPAddress.Parse(address.Host);
            port = address.Port;
        }

        private static async Task ProcessClient<TContext>(IHttpApplication<TContext> application, MemoryPool pool, Socket socket)
        {
            using (var ns = new NetworkStream(socket))
            {
                // var id = Guid.NewGuid();
                var channelFactory = new ChannelFactory(pool);
                var input = channelFactory.MakeReadableChannel(ns);
                var output = channelFactory.MakeWriteableChannel(ns);
                // output = channelFactory.MakeWriteableChannel(output, Dump);
                // input = channelFactory.MakeReadableChannel(input, Dump);

                var connection = new HttpConnection<TContext>(application, input, output, channelFactory);

                // Console.WriteLine($"[{id}]: Connection started");

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

                // Console.WriteLine($"[{id}]: Connection ended");

                output.CompleteWriting();
                input.CompleteReading();

                //GC.Collect();
                //GC.WaitForPendingFinalizers();
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
