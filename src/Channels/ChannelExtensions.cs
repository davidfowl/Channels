// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
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
            while (true)
            {
                var awaiter = input.ReadAsync();

                if (!awaiter.IsCompleted)
                {
                    break;
                }

                var fin = input.Completion.IsCompleted;

                var inputBuffer = awaiter.GetResult();
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
                var inputBuffer = await input.ReadAsync();

                var fin = input.Completion.IsCompleted;

                try
                {
                    if (inputBuffer.IsEmpty && fin)
                    {
                        return;
                    }

                    foreach (var span in inputBuffer)
                    {
                        ArraySegment<byte> buffer;

                        unsafe
                        {
                            if (!span.TryGetArray(null, out buffer))
                            {
                                // Fall back to copies if this was native memory and we were unable to get
                                //  something we could write
                                buffer = new ArraySegment<byte>(span.CreateArray());
                            }
                        }

                        await stream.WriteAsync(buffer.Array, buffer.Offset, span.Length);

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
                var inputBuffer = await input.ReadAsync();

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
                var inputBuffer = await input.ReadAsync();

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
