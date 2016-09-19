﻿using System;
using System.Net;
using System.Threading.Tasks;
using Channels.Networking.Libuv;
using Channels.Text.Primitives;

namespace Channels.Samples
{
    public class RawLibuvHttpClientSample
    {
        public static async Task Run()
        {
            var thread = new UvThread();
            var client = new UvTcpClient(thread, new IPEndPoint(IPAddress.Loopback, 5000));

            var consoleOutput = thread.ChannelFactory.MakeWriteableChannel(Console.OpenStandardOutput());

            var connection = await client.ConnectAsync();

            while (true)
            {
                var buffer = connection.Output.Alloc();

                buffer = buffer.WriteAsciiString("GET / HTTP/1.1");
                buffer = buffer.WriteAsciiString("\r\n\r\n");

                await buffer.FlushAsync();

                // Write the client output to the console
                await CopyCompletedAsync(connection.Input, consoleOutput);

                await Task.Delay(1000);
            }
        }
        private static async Task CopyCompletedAsync(IReadableChannel input, IWritableChannel channel)
        {
            var inputBuffer = await input.ReadAsync();

            while (true)
            {
                try
                {
                    if (inputBuffer.IsEmpty && input.Reading.IsCompleted)
                    {
                        return;
                    }

                    var buffer = channel.Alloc();

                    buffer.Append(ref inputBuffer);

                    await buffer.FlushAsync();
                }
                finally
                {
                    inputBuffer.Consumed();
                }

                var awaiter = input.ReadAsync();

                if (!awaiter.IsCompleted)
                {
                    // No more data
                    break;
                }

                inputBuffer = await awaiter;
            }
        }

    }
}
