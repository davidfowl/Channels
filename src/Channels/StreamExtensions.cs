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
                var end = channel.BeginWrite();
                var block = end.Block;

                try
                {
                    int bytesRead = await stream.ReadAsync(block.Array, block.End, block.Data.Offset + block.Data.Count - block.End);

                    if (bytesRead == 0)
                    {
                        channel.CompleteWriting();
                        break;
                    }
                    else
                    {
                        end.UpdateWritten(bytesRead);
                        await channel.EndWriteAsync(end);
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
