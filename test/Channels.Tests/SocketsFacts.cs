using Channels.Networking.Libuv;
using Channels.Networking.Sockets;
using Channels.Networking.Windows.RIO;
using Channels.Text.Primitives;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class SocketsFacts
    {
        [Fact]
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

                GC.Collect();
                GC.WaitForPendingFinalizers();
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

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.Equal(MessageToSend, reply);
        }

        // [Fact]
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
            }
            Assert.Equal(MessageToSend, reply);
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

        private void Echo(SocketConnection connection)
        {
            Echo(connection.Input, connection.Output, connection);
        }

        private void Echo(UvTcpConnection connection)
        {
            Echo(connection.Input, connection.Output, null);
        }

        private async void Echo(IReadableChannel input, IWritableChannel output, IDisposable lifetime)
        {
            try
            {
                while (true)
                {
                    var request = await input.ReadAsync();

                    try
                    {
                        if (request.IsEmpty && input.Completion.IsCompleted)
                        {
                            break;
                        }

                        var response = output.Alloc();
                        response.Append(ref request);
                        await response.FlushAsync();
                    }
                    finally
                    {
                        request.Consumed();
                    }
                }
            }
            finally
            {
                input.CompleteReading();

                output.CompleteWriting();

                lifetime?.Dispose();
            }
        }
    }
}
