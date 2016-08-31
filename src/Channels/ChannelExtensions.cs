// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Channels
{
    public static class WritableChannelExtensions
    {
        public static Task WriteAsync(this IWritableChannel channel, byte[] buffer, int offset, int count)
        {
            var writeBuffer = channel.Alloc();
            writeBuffer.Write(buffer, offset, count);
            return writeBuffer.FlushAsync();
        }

        public static Task WriteAsync(this IWritableChannel channel, ArraySegment<byte> buffer)
        {
            return channel.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);
        }
    }

    public static class ReadableChannelExtensions
    {
        public static ValueTask<int> ReadAsync(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (input.IsCompleted)
            {
                var fin = input.Completion.IsCompleted;

                var inputBuffer = input.GetResult();
                var sliced = inputBuffer.Slice(0, count);
                sliced.CopyTo(buffer, offset);
                int actual = sliced.Length;
                inputBuffer.Consumed(sliced.End);

                if (actual != 0)
                {
                    return new ValueTask<int>(actual);
                }
                else if (fin)
                {
                    return new ValueTask<int>(0);
                }
            }

            return new ValueTask<int>(input.ReadAsyncAwaited(buffer, offset, count));
        }

        public static async Task CopyToAsync(this IReadableChannel input, Stream stream)
        {
            while (true)
            {
                var inputBuffer = await input;

                var fin = input.Completion.IsCompleted;

                try
                {
                    if (inputBuffer.IsEmpty && fin)
                    {
                        return;
                    }

                    foreach (var span in inputBuffer)
                    {
                        await stream.WriteAsync(span.Array, span.Offset, span.Length);
                    }
                }
                finally
                {
                    inputBuffer.Consumed();
                }
            }
        }

        public static async Task CopyToAsync(this IReadableChannel input, IWritableChannel output)
        {
            while (true)
            {
                var inputBuffer = await input;

                var fin = input.Completion.IsCompleted;

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
                    inputBuffer.Consumed();
                }
            }
        }

        private static async Task<int> ReadAsyncAwaited(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (true)
            {
                var inputBuffer = await input;

                var fin = input.Completion.IsCompleted;

                var sliced = inputBuffer.Slice(0, count);
                sliced.CopyTo(buffer, offset);
                int actual = sliced.Length;
                inputBuffer.Consumed(sliced.End);

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
