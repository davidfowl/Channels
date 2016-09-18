﻿using Channels.Networking.Libuv;
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

        //[Fact]
        public void CanCreateWorkingEchoServer_ChannelLibuvServer_NonChannelClient()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5010);
            const string MessageToSend = "Hello world!";
            string reply = null;

            using (var thread = new UvThread())
            {
                var server = new UvTcpListener(thread, endpoint);
                server.OnConnection(Echo);
                server.Start();
                try
                {
                    reply = SendBasicSocketMessage(endpoint, MessageToSend);
                }
                finally
                {
                    server.Stop();
                }
            }
            Assert.Equal(MessageToSend, reply);
        }

        [Fact]
        public async Task CanCreateWorkingEchoServer_ChannelSocketServer_ChannelSocketClient()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5010);
            const string MessageToSend = "Hello world!";
            string reply = null;

            using (var server = new SocketListener())
            {
                server.OnConnection(Echo);
                server.Start(endpoint);


                using (var client = await SocketConnection.ConnectAsync(endpoint))
                {
                    var output = client.Output.Alloc();
                    WritableBufferExtensions.WriteUtf8String(ref output, MessageToSend);
                    await output.FlushAsync();
                    client.Output.Complete();

                    while (true)
                    {
                        var input = await client.Input.ReadAsync();
                        // wait for the end of the data before processing anything
                        if (client.Input.Reading.IsCompleted)
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
            }
            Assert.Equal(MessageToSend, reply);
        }

        [Fact]
        public void CanCreateWorkingEchoServer_ChannelSocketServer_NonChannelClient()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5010);
            const string MessageToSend = "Hello world!";
            string reply = null;

            using (var server = new SocketListener())
            {
                server.OnConnection(Echo);
                server.Start(endpoint);

                reply = SendBasicSocketMessage(endpoint, MessageToSend);
            }
            Assert.Equal(MessageToSend, reply);
        }

        //[Fact]
        public async Task RunStressPingPongTest_Libuv()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5020);

            using (var thread = new UvThread())
            {
                var server = new UvTcpListener(thread, endpoint);
                server.OnConnection(PongServer);
                server.Start();

                const int SendCount = 500, ClientCount = 5;
                for (int loop = 0; loop < ClientCount; loop++)
                {
                    using (var client = await new UvTcpClient(thread, endpoint).ConnectAsync())
                    {
                        var tuple = await PingClient(client, SendCount);
                        Assert.Equal(SendCount, tuple.Item1);
                        Assert.Equal(SendCount, tuple.Item2);
                        Console.WriteLine($"Ping: {tuple.Item1}; Pong: {tuple.Item2}; Time: {tuple.Item3}ms");
                    }
                }
            }
        }

        //[Fact]
        public async Task RunStressPingPongTest_Socket()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5020);

            using (var server = new SocketListener())
            {
                server.OnConnection(PongServer);
                server.Start(endpoint);

                const int SendCount = 500, ClientCount = 5;
                for (int loop = 0; loop < ClientCount; loop++)
                {
                    using (var client = await SocketConnection.ConnectAsync(endpoint))
                    {
                        var tuple = await PingClient(client, SendCount);
                        Assert.Equal(SendCount, tuple.Item1);
                        Assert.Equal(SendCount, tuple.Item2);
                        Console.WriteLine($"Ping: {tuple.Item1}; Pong: {tuple.Item2}; Time: {tuple.Item3}ms");
                    }
                }
            }
        }

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
                    if (inputBuffer.IsEmpty && channel.Input.Reading.IsCompleted)
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
            channel.Input.Complete();
            channel.Output.Complete();
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
                    if (inputBuffer.IsEmpty && channel.Input.Reading.IsCompleted)
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
                channel.Input.Complete();
                channel.Output.Complete();
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
                    if (request.IsEmpty && channel.Input.Reading.IsCompleted)
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
                channel.Input.Complete();
                channel.Output.Complete();
            }
            catch (Exception ex)
            {
                if (!(channel.Input?.Reading?.IsCompleted ?? true)) channel.Input.Complete(ex);
                if (!(channel.Output?.Writing?.IsCompleted ?? true)) channel.Output.Complete(ex);
            }
            finally
            {
                channel?.Dispose();
            }
        }
    }
}
