using System;
using System.IO;
using System.Threading.Tasks;

namespace Channels
{
    public static class StreamExtensions
    {
        public static async Task CopyToAsync(this Stream stream, IWritableChannel channel)
        {
            byte[] managed = null;

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
                            // The span is backed by native memory so we need to use a managed array to read
                            // from the stream and copy that back to the native buffer
                            if (managed == null)
                            {
                                managed = new byte[2048];
                            }

                            data = new ArraySegment<byte>(managed);
                        }
                    }

                    int bytesRead = await stream.ReadAsync(data.Array, data.Offset, data.Count);

                    if (managed != null)
                    {
                        buffer.Write(managed, 0, bytesRead);
                    }
                    else
                    {
                        buffer.CommitBytes(bytesRead);
                    }

                    if (bytesRead == 0)
                    {
                        channel.CompleteWriting();
                        break;
                    }
                    else
                    {
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
