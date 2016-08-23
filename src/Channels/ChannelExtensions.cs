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
            return channel.WriteAsync(writeBuffer);
        }

        public static Task WriteAsync(this IWritableChannel channel, ArraySegment<byte> buffer)
        {
            return channel.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);
        }
    }

    public static class ReadableChannelExtensions
    {
        public static void EndRead(this IReadableChannel input, ReadIterator consumed)
        {
            input.EndRead(consumed, consumed);
        }

        public static void EndRead(this IReadableChannel input, ReadableBuffer consumed)
        {
            input.EndRead(consumed.End, consumed.End);
        }

        public static ValueTask<int> ReadAsync(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (input.IsCompleted)
            {
                var fin = input.Completion.IsCompleted;

                var inputBuffer = input.BeginRead();
                var sliced = inputBuffer.Slice(0, count);
                sliced.CopyTo(buffer, offset);
                int actual = sliced.Length;
                input.EndRead(sliced);

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
                await input;

                var fin = input.Completion.IsCompleted;

                var inputBuffer = input.BeginRead();

                try
                {
                    if (inputBuffer.Length == 0 && fin)
                    {
                        return;
                    }

                    foreach (var span in inputBuffer.GetSpans())
                    {
                        await stream.WriteAsync(span.Array, span.Offset, span.Length);
                    }
                }
                catch (Exception)
                {
                    // REVIEW: Should we do anything here?
                }
                finally
                {
                    input.EndRead(inputBuffer);
                }
            }
        }

        public static async Task CopyToAsync(this IReadableChannel input, IWritableChannel channel)
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
                        return;
                    }

                    var buffer = channel.Alloc();

                    buffer.Append(inputBuffer);

                    await channel.WriteAsync(buffer);
                }
                finally
                {
                    input.EndRead(inputBuffer);
                }
            }
        }

        private static async Task<int> ReadAsyncAwaited(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (true)
            {
                await input;

                var fin = input.Completion.IsCompleted;

                var inputBuffer = input.BeginRead();
                var sliced = inputBuffer.Slice(0, count);
                sliced.CopyTo(buffer, offset);
                int actual = sliced.Length;
                input.EndRead(sliced);

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
