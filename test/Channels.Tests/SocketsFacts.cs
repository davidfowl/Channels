using Channels.Networking.Sockets;
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
        public void CanCreateWorkingEchoServer_Channel_Server_NonChannel_Client()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5010);
            const string MessageToSend = "Hello world!";
            string reply = null;

            using (var server = new SocketListener())
            {
                server.OnConnection(Echo);
                server.Start(endpoint);

                // create the client the old way
                using (var socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Connect(endpoint);
                    var data = Encoding.UTF8.GetBytes(MessageToSend);
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
                    reply = Encoding.UTF8.GetString(buffer, 0, offset);
                }
            }
            Assert.Equal(MessageToSend, reply);
        }

        [Fact]
        public async Task CanCreateWorkingEchoServer_Channel_Client_Server()
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



        private async void Echo(SocketConnection connection)
        {
            using (connection)
            {
                try
                {
                    while (true)
                    {
                        ReadableBuffer request = await connection.Input.ReadAsync();
                        if (request.IsEmpty && connection.Input.Completion.IsCompleted)
                        {
                            request.Consumed();
                            break;
                        }

                        int len = request.Length;
                        var response = connection.Output.Alloc();
                        response.Append(ref request);
                        await response.FlushAsync();
                        request.Consumed();
                    }
                    connection.Input.CompleteReading();
                    connection.Output.CompleteWriting();
                }
                catch (Exception ex)
                {
                    if (!(connection?.Input?.Completion?.IsCompleted ?? true)) connection.Input.CompleteReading(ex);
                    if (!(connection?.Output?.Completion?.IsCompleted ?? true)) connection.Output.CompleteWriting(ex);
                }
            }
        }
    }
}
