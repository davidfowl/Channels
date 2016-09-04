using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                    ArraySegment<byte> data;
                    unsafe
                    {
                        if (!buffer.Memory.TryGetArray(null, out data))
                        {
                            Debug.Assert(false, "This should never fail. We always allocate managed memory");
                        }
                    }

                    int bytesRead = await stream.ReadAsync(data.Array, data.Offset, data.Count);

                    if (bytesRead == 0)
                    {
                        channel.CompleteWriting();
                        break;
                    }
                    else
                    {
                        buffer.CommitBytes(bytesRead);
                        await buffer.FlushAsync();
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
