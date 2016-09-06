using Channels.Networking.Sockets;
using System.Net;
using Xunit;
using System;
using System.Threading.Tasks;
using Channels.Text.Primitives;

namespace Channels.Tests
{
    public class SocketsFacts
    {
        [Fact]
        public async Task CanCreateWorkingEchoServer()
        {
            var endpoint = new IPEndPoint(IPAddress.Loopback, 5010);
            const string MessageToSend = "Hello world!";
            string reply = null;

            using (var server = new SocketListener())
            {
                server.OnConnection(OnConnection);
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

        private async void OnConnection(SocketConnection connection)
        {
            using (connection)
            {
                try
                {
                    Console.WriteLine("[server] OnConnection entered");
                    while (true)
                    {
                        ReadableBuffer request;

                        Console.WriteLine("[server] awaiting input...");
                        try
                        {
                            request = await connection.Input.ReadAsync();
                        }
                        finally
                        {
                            Console.WriteLine("[server] await completed");
                        }
                        if (request.IsEmpty && connection.Input.Completion.IsCompleted) break;

                        int len = request.Length;
                        Console.WriteLine($"[server] echoing {len} bytes...");
                        var response = connection.Output.Alloc();
                        response.Append(ref request);
                        await response.FlushAsync();
                        Console.WriteLine($"[server] echoed");
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
                finally
                {
                    Console.WriteLine("[server] OnConnection exited");
                }
            }
        }
    }
}
