// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public static class ChannelExtensions
    {
        public static Stream GetStream(this IChannel channel)
        {
            return new ChannelStream(channel);
        }
    }

    public static class WritableChannelExtensions
    {
        public static Task WriteAsync(this IWritableChannel channel, Span<byte> source)
        {
            var writeBuffer = channel.Alloc();
            writeBuffer.Write(source);
            return writeBuffer.FlushAsync();
        }
    }

    public static class ReadableChannelExtensions
    {
        public static void Advance(this IReadableChannel input, ReadCursor cursor)
        {
            input.Advance(cursor, cursor);
        }

        public static ValueTask<int> ReadAsync(this IReadableChannel input, Span<byte> destination)
        {
            while (true)
            {
                var awaiter = input.ReadAsync();

                if (!awaiter.IsCompleted)
                {
                    break;
                }

                var fin = input.Reading.IsCompleted;

                var inputBuffer = awaiter.GetResult();
                var sliced = inputBuffer.Slice(0, destination.Length);
                sliced.CopyTo(destination);
                int actual = sliced.Length;
                input.Advance(sliced.End);

                if (actual != 0)
                {
                    return new ValueTask<int>(actual);
                }
                else if (fin)
                {
                    return new ValueTask<int>(0);
                }
            }

            return new ValueTask<int>(input.ReadAsyncAwaited(destination));
        }

        public static Task CopyToAsync(this IReadableChannel input, Stream stream)
        {
            return input.CopyToAsync(stream, 4096, CancellationToken.None);
        }

        public static async Task CopyToAsync(this IReadableChannel input, Stream stream, int bufferSize, CancellationToken cancellationToken)
        {
            // TODO: Use bufferSize argument
            while (!cancellationToken.IsCancellationRequested)
            {
                var inputBuffer = await input.ReadAsync();

                try
                {
                    if (inputBuffer.IsEmpty && input.Reading.IsCompleted)
                    {
                        return;
                    }

                    foreach (var memory in inputBuffer)
                    {
                        ArraySegment<byte> buffer;

                        if (!memory.TryGetArray(out buffer))
                        {
                            // Fall back to copies if this was native memory and we were unable to get
                            //  something we could write
                            buffer = new ArraySegment<byte>(memory.Span.CreateArray());
                        }

                        await stream.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);

                    }
                }
                finally
                {
                    input.Advance(inputBuffer.End);
                }
            }
        }

        public static async Task CopyToAsync(this IReadableChannel input, IWritableChannel output)
        {
            while (true)
            {
                var inputBuffer = await input.ReadAsync();

                var fin = input.Reading.IsCompleted;

                try
                {
                    if (inputBuffer.IsEmpty && fin)
                    {
                        return;
                    }

                    var buffer = output.Alloc();

                    buffer.Append(ref inputBuffer);

                    await buffer.FlushAsync();
                }
                finally
                {
                    input.Advance(inputBuffer.End);
                }
            }
        }

        private static async Task<int> ReadAsyncAwaited(this IReadableChannel input, Span<byte> destination)
        {
            while (true)
            {
                var inputBuffer = await input.ReadAsync();

                var fin = input.Reading.IsCompleted;

                var sliced = inputBuffer.Slice(0, destination.Length);
                sliced.CopyTo(destination);
                int actual = sliced.Length;
                input.Advance(sliced.End);

                if (actual != 0)
                {
                    return actual;
                }
                else if (fin)
                {
                    return 0;
                }
            }
        }
    }
}
