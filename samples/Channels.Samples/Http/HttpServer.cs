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
        private RioTcpServer _rioTcpServer;

        public HttpServer()
        {
            Features.Set<IServerAddressesFeature>(new ServerAddressesFeature());
        }

        public void Start<TContext>(IHttpApplication<TContext> application)
        {
            var feature = Features.Get<IServerAddressesFeature>();
            var address = feature.Addresses.FirstOrDefault();
            IPAddress ip;
            int port;
            GetIp(address, out ip, out port);
            Task.Run(() => StartAcceptingRIOConnections(application, ip, port));
        }

        private void StartAcceptingRIOConnections<TContext>(IHttpApplication<TContext> application, IPAddress ip, int port)
        {
            var addressBytes = ip.GetAddressBytes();
            _rioTcpServer = new RioTcpServer((ushort)port, addressBytes[0], addressBytes[1], addressBytes[2], addressBytes[3]);

            while (true)
            {
                try
                {
                    var connection = _rioTcpServer.Accept();
                    var task = Task.Factory.StartNew(() => ProcessRIOConnection(application, connection));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    break;
                }
            }
        }

        private async void StartAcceptingConnections<TContext>(IHttpApplication<TContext> application, IPAddress ip, int port)
        {
            _listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(new IPEndPoint(ip, port));
            _listenSocket.Listen(10);

            using (var pool = new MemoryPool())
            {
                var channelFactory = new ChannelFactory(pool);

                while (true)
                {
                    try
                    {
                        var clientSocket = await _listenSocket.AcceptAsync();
                        clientSocket.NoDelay = true;
                        var task = Task.Factory.StartNew(() => ProcessConnection(application, channelFactory, clientSocket));
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
            _rioTcpServer?.Stop();
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

        private static async Task ProcessRIOConnection<TContext>(IHttpApplication<TContext> application, RioTcpConnection connection)
        {
            using (connection)
            {
                await ProcessClient(application, connection.ChannelFactory, connection.Input, connection.Output);
            }
        }

        private static async Task ProcessConnection<TContext>(IHttpApplication<TContext> application, ChannelFactory channelFactory, Socket socket)
        {
            using (var ns = new NetworkStream(socket))
            {
                var input = channelFactory.MakeReadableChannel(ns);
                var output = channelFactory.MakeWriteableChannel(ns);

                await ProcessClient(application, channelFactory, input, output);
            }
        }

        private static async Task ProcessClient<TContext>(IHttpApplication<TContext> application, ChannelFactory channelFactory, IReadableChannel input, IWritableChannel output)
        {
            // var id = Guid.NewGuid();
            // output = channelFactory.MakeWriteableChannel(output, Dump);
            // input = channelFactory.MakeReadableChannel(input, Dump);

            var connection = new HttpConnection<TContext>(application, input, output);

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

        private static async Task Dump(IReadableChannel input, IWritableChannel output)
        {
            while (true)
            {
                await input;

                var fin = input.Completion.IsCompleted;

                var inputBuffer = input.BeginRead();

                try
                {
                    if (inputBuffer.Length == 0 && fin)
                    {
                        break;
                    }

                    foreach (var span in inputBuffer.GetSpans())
                    {
                        Console.Write(Encoding.UTF8.GetString(span.Array, span.Offset, span.Length));
                    }

                    var buffer = output.Alloc();

                    buffer.Append(inputBuffer);

                    await output.WriteAsync(buffer);
                }
                finally
                {
                    input.EndRead(inputBuffer);
                }
            }

            input.CompleteReading();
            output.CompleteWriting();
        }
    }
}
