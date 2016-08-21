using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public static class StreamExtensions
    {
        public static async Task CopyToAsync(this Stream stream, IWritableChannel channel)
        {
            while (true)
            {
                var buffer = channel.Alloc(2048);

                try
                {
                    int bytesRead = await stream.ReadAsync(buffer.Memory.Buffer.Array, buffer.Memory.Buffer.Offset, buffer.Memory.Buffer.Count);

                    if (bytesRead == 0)
                    {
                        channel.CompleteWriting();
                        break;
                    }
                    else
                    {
                        buffer.UpdateWritten(bytesRead);
                        await channel.WriteAsync(buffer);
                    }
                }
                catch (Exception error)
                {
                    channel.CompleteWriting(error);
                    break;
                }
            }
        }
    }
}
