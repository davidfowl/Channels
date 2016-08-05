// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace Channels
{
    public static class WritableChannelExtensions
    {
        public static Task WriteAsync(this IWritableChannel channel, byte[] buffer, int offset, int length)
        {
            var end = channel.BeginWrite();
            end.CopyFrom(buffer, offset, length);
            return channel.EndWriteAsync(end);
        }

        public static Task WriteAsync(this IWritableChannel channel, ArraySegment<byte> buffer)
        {
            return channel.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);
        }
    }

    public static class ReadableChannelExtensions
    {
        public static void EndRead(this IReadableChannel input, MemoryPoolIterator consumed)
        {
            input.EndRead(consumed, consumed);
        }

        public static ValueTask<int> ReadAsync(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (input.IsCompleted)
            {
                var fin = input.Completion.IsCompleted;

                var begin = input.BeginRead().Begin;
                int actual;
                var end = begin.CopyTo(buffer, offset, count, out actual);
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

                var span = input.BeginRead();

                if (span.Begin.IsEnd && fin)
                {
                    return;
                }

                try
                {
                    await span.Begin.CopyToAsync(stream, span.End);
                }
                finally
                {
                    input.EndRead(span.End);
                }
            }
        }

        public static async Task CopyToAsync(this IReadableChannel input, IWritableChannel channel)
        {
            // REVIEW: If the blocks returned are from the same pool can do do something crazy like
            // transfer blocks directly to the writable channel
            while (true)
            {
                await input;

                var fin = input.Completion.IsCompleted;

                var span = input.BeginRead();

                if (span.Begin.IsEnd && fin)
                {
                    return;
                }

                try
                {
                    await span.Begin.CopyToAsync(channel, span.End);
                }
                finally
                {
                    input.EndRead(span.End);
                }
            }
        }

        private static async Task<int> ReadAsyncAwaited(this IReadableChannel input, byte[] buffer, int offset, int count)
        {
            while (true)
            {
                await input;

                var fin = input.Completion.IsCompleted;

                var begin = input.BeginRead().Begin;
                int actual;
                var end = begin.CopyTo(buffer, offset, count, out actual);
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
