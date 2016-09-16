using Channels.Networking;
using Channels.Networking.Libuv;
using Channels.Networking.Sockets;
using Channels.Text.Primitives;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class SocketsFacts
    {

        static readonly Span<byte> _ping = new Span<byte>(Encoding.ASCII.GetBytes("PING")), _pong = new Span<byte>(Encoding.ASCII.GetBytes("PING"));

        [Fact(Skip = "glitching")]
        public Task CanCreateWorkingEchoServer_ChannelLibuvServer_NonChannelClient()
            => EchoTest_NonChannelClient<UvTcpTransportProvider>("127.0.0.1:5010");

        private async Task EchoTest_NonChannelClient<TServerProvider>(string configuration, string messageToSend = "Hello world!")
            where TServerProvider : TransportProvider, new()
        {
            string reply;
            using (var provider = new TServerProvider())
            using (var server = await provider.StartServerAsync(configuration, Echo))
            {
                var endpoint = await TransportProvider.ParseIPEndPoint(configuration);
                reply = SendBasicSocketMessage(endpoint, messageToSend);
            }
            Assert.Equal(messageToSend, reply);
        }

        [Theory]
        [InlineData("127.0.0.1:5010")]
        [InlineData("localhost:5010")]
        [InlineData("[::1]:5010", Skip = "disabled because: linux")]
        [InlineData("[0000:0000:0000:0000:0000:0000:0000:0001]:5010", Skip = "disabled because: linux")]
        public Task CanCreateWorkingEchoServer_ChannelSocketServer_ChannelSocketClient(string config)
            => RunEchoTest<SocketTransportProvider, SocketTransportProvider>(config);

        [Fact]
        public Task CanCreateWorkingEchoServer_DirectClientServer()
            => RunEchoTest<DirectTransportProvider, DirectTransportProvider>("foo");

        private async Task RunEchoTest<TServerProvider, TClientProvider>(string config, string messageToSend = "Hello world!")
            where TServerProvider : TransportProvider, new()
            where TClientProvider : TransportProvider, new()
        {
            string reply = null;

            using (TransportProvider serverProvider = new TServerProvider())
            using (TransportProvider clientProvider = // re-use if possible
                typeof(TServerProvider) == typeof(TClientProvider) ? serverProvider : new TClientProvider())
            using (var server = await serverProvider.StartServerAsync(config, Echo))
            using (var client = await clientProvider.ConnectAsync(config))
            {
                var output = client.Output.Alloc();
                WritableBufferExtensions.WriteUtf8String(ref output, messageToSend);
                await output.FlushAsync();
                client.Output.CompleteWriting();

                while (true)
                {
                    var input = await client.Input.ReadAsync();
                    // wait for the end of the data before processing anything
                    if (client.Input.Completion.IsCompleted)
                    {
                        reply = input.GetUtf8String();
                        input.Consumed();
                        break;
                    }
                    else
                    {
                        input.Consumed(input.Start, input.End);
                    }
                }
            }
            Assert.Equal(messageToSend, reply);
        }


        [Fact]
        public Task CanCreateWorkingEchoServer_ChannelSocketServer_NonChannelClient()
            => EchoTest_NonChannelClient<SocketTransportProvider>("127.0.0.1:5010");

        [Fact]
        public Task RunStressPingPongTest_Direct()
            => RunPingPongTest<DirectTransportProvider, DirectTransportProvider>("foo", messagesPerClient: 50000);


        private async Task RunPingPongTest<TServerProvider, TClientProvider>(string config, int messagesPerClient = 1000, int clientCount = 5)
            where TServerProvider : TransportProvider, new()
            where TClientProvider : TransportProvider, new()
        {

            using (TransportProvider serverProvider = new TServerProvider())
            using (TransportProvider clientProvider = // re-use if possible
                typeof(TServerProvider) == typeof(TClientProvider) ? serverProvider : new TClientProvider())
            using (var server = await serverProvider.StartServerAsync(config, PongServer))
            {
                for (int loop = 0; loop < clientCount; loop++)
                {
                    using (var client = await clientProvider.ConnectAsync(config))
                    {
                        var tuple = await PingClient(client, messagesPerClient);
                        Assert.Equal(messagesPerClient, tuple.Item1);
                        Assert.Equal(messagesPerClient, tuple.Item2);
                        Console.WriteLine($"Ping ({client.GetType().Name}): {tuple.Item1}; Pong ({server.GetType().Name}): {tuple.Item2}; Time: {tuple.Item3}ms, {(messagesPerClient * 1000M) / tuple.Item3:#.00}/s");
                    }
                }
            }
        }

        [Fact(Skip = "glitching")]
        public Task RunStressPingPongTest_Libuv()
            => RunPingPongTest<UvTcpTransportProvider, UvTcpTransportProvider>("127.0.0.1:5020");

        [Fact]
        public Task RunStressPingPongTest_Socket()
            => RunPingPongTest<SocketTransportProvider, SocketTransportProvider>("127.0.0.1:5020");

        static async Task<Tuple<int, int, int>> PingClient(IChannel channel, int messagesToSend)
        {
            int count = 0;
            var watch = Stopwatch.StartNew();
            int sendCount = 0, replyCount = 0;
            for (int i = 0; i < messagesToSend; i++)
            {
                await channel.Output.WriteAsync(_ping);
                sendCount++;

                bool havePong = false;
                while (true)
                {
                    var inputBuffer = await channel.Input.ReadAsync();
                    if (inputBuffer.IsEmpty && channel.Input.Completion.IsCompleted)
                    {
                        inputBuffer.Consumed(inputBuffer.End);
                        break;
                    }
                    if (inputBuffer.Length < 4)
                    {
                        inputBuffer.Consumed(inputBuffer.Start, inputBuffer.End);
                    }
                    else
                    {
                        havePong = inputBuffer.Equals(_ping);
                        if (havePong)
                        {
                            count++;
                        }
                        inputBuffer.Consumed(inputBuffer.End);
                        break;
                    }
                }

                if (havePong)
                {
                    replyCount++;
                }
                else
                {
                    break;
                }
            }
            channel.Input.CompleteReading();
            channel.Output.CompleteWriting();
            watch.Stop();

            return Tuple.Create(sendCount, replyCount, (int)watch.ElapsedMilliseconds);

        }

        private static async void PongServer(IChannel channel)
        {
            try
            {
                while (true)
                {
                    var inputBuffer = await channel.Input.ReadAsync();
                    if (inputBuffer.IsEmpty && channel.Input.Completion.IsCompleted)
                    {
                        inputBuffer.Consumed(inputBuffer.End);
                        break;
                    }
                    if (inputBuffer.Length < 4)
                    {
                        inputBuffer.Consumed(inputBuffer.Start, inputBuffer.End);
                    }
                    else
                    {
                        bool isPing = inputBuffer.Equals(_ping);
                        if (isPing)
                        {
                            await channel.Output.WriteAsync(_pong);
                        }
                        else
                        {
                            break;
                        }
                        inputBuffer.Consumed(inputBuffer.End);
                    }
                }
                channel.Input.CompleteReading();
                channel.Output.CompleteWriting();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                channel?.Dispose();
            }
        }

        private static string SendBasicSocketMessage(IPEndPoint endpoint, string message)
        {
            // create the client the old way
            using (var socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Connect(endpoint);
                var data = Encoding.UTF8.GetBytes(message);
                socket.Send(data);
                socket.Shutdown(SocketShutdown.Send);

                byte[] buffer = new byte[data.Length];
                int offset = 0, bytesReceived;
                while (offset <= buffer.Length
                    && (bytesReceived = socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None)) > 0)
                {
                    offset += bytesReceived;
                }
                socket.Shutdown(SocketShutdown.Receive);
                return Encoding.UTF8.GetString(buffer, 0, offset);
            }
        }
        private async void Echo(IChannel channel)
        {
            try
            {
                while (true)
                {
                    ReadableBuffer request = await channel.Input.ReadAsync();
                    if (request.IsEmpty && channel.Input.Completion.IsCompleted)
                    {
                        request.Consumed();
                        break;
                    }

                    int len = request.Length;
                    var response = channel.Output.Alloc();
                    response.Append(ref request);
                    await response.FlushAsync();
                    request.Consumed();
                }
                channel.Input.CompleteReading();
                channel.Output.CompleteWriting();
            }
            catch (Exception ex)
            {
                if (!(channel.Input?.Completion?.IsCompleted ?? true)) channel.Input.CompleteReading(ex);
                if (!(channel.Output?.Completion?.IsCompleted ?? true)) channel.Output.CompleteWriting(ex);
            }
            finally
            {
                channel?.Dispose();
            }
        }
    }
}
