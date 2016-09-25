using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Channels.Samples
{
    public class ChannelHttpContent : HttpContent
    {
        private readonly IReadableChannel _output;

        public ChannelHttpContent(IReadableChannel output)
        {
            _output = output;
        }

        public int ContentLength { get; set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            int remaining = ContentLength;

            while (remaining > 0)
            {
                var inputBuffer = await _output.ReadAsync();

                var fin = _output.Reading.IsCompleted;

                var consumed = inputBuffer.Start;

                try
                {
                    if (inputBuffer.IsEmpty && fin)
                    {
                        return;
                    }

                    var data = inputBuffer.Slice(0, remaining);

                    foreach (var memory in data)
                    {
                        ArraySegment<byte> buffer;

                        unsafe
                        {
                            if (!memory.TryGetArray(out buffer))
                            {
                                // Fall back to copies if this was native memory and we were unable to get
                                //  something we could write
                                buffer = new ArraySegment<byte>(memory.Span.CreateArray());
                            }
                        }

                        await stream.WriteAsync(buffer.Array, buffer.Offset, buffer.Count);
                    }

                    consumed = data.End;
                    remaining -= data.Length;
                }
                finally
                {
                    _output.Advance(consumed);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = ContentLength;
            return true;
        }
    }
}
