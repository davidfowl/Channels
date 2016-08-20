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
            var end = channel.Alloc();
            end.Write(buffer, offset, count);
            return channel.WriteAsync(end);
        }

        public static Task WriteAsync(this IWritableChannel channel, ArraySegment<byte> buffer)
        {
            return channel.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);
        }
    }

    public static class ReadableChannelExtensions
    {
        public static void EndRead(this IReadableChannel input, ReadableBuffer consumed)
        {
            input.EndRead(consumed, consumed);
        }

        public static ValueTask<int> ReadAsync(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (input.IsCompleted)
            {
                var fin = input.Completion.IsCompleted;

                var begin = input.BeginRead();
                var end = begin;
                int actual = end.CopyTo(buffer, offset, count);
                input.EndRead(end);

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

                var begin = input.BeginRead();
                var end = begin;

                try
                {
                    if (begin.IsEnd && fin)
                    {
                        return;
                    }

                    BufferSpan span;
                    while (end.TryGetBuffer(out span))
                    {
                        await stream.WriteAsync(span.Buffer.Array, span.Buffer.Offset, span.Buffer.Count);
                    }
                }
                finally
                {
                    input.EndRead(end);
                }
            }
        }

        public static async Task CopyToAsync(this IReadableChannel input, IWritableChannel channel, Action<BufferSpan> onData = null)
        {
            while (true)
            {
                await input;

                var fin = input.Completion.IsCompleted;

                var begin = input.BeginRead();
                var end = begin;

                try
                {
                    if (begin.IsEnd && fin)
                    {
                        return;
                    }

                    var buffer = channel.Alloc();

                    BufferSpan span;
                    while (end.TryGetBuffer(out span))
                    {
                        onData?.Invoke(span);
                        buffer.Append(begin, end);
                        begin = end;
                    }

                    await channel.WriteAsync(buffer);
                }
                finally
                {
                    input.EndRead(end);
                }
            }
        }

        private static async Task<int> ReadAsyncAwaited(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (true)
            {
                await input;

                var fin = input.Completion.IsCompleted;

                var begin = input.BeginRead();
                var end = begin;
                int actual = begin.CopyTo(buffer, offset, count);
                input.EndRead(end);

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
