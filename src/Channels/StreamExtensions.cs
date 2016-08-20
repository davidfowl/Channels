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
                var end = channel.Alloc(2048);

                try
                {
                    int bytesRead = await stream.ReadAsync(end.Memory.Buffer.Array, end.Memory.Buffer.Offset, end.Memory.Buffer.Count);

                    if (bytesRead == 0)
                    {
                        channel.CompleteWriting();
                        break;
                    }
                    else
                    {
                        end.UpdateWritten(bytesRead);
                        await channel.WriteAsync(end);
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
