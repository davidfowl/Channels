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
                await _output;

                var fin = _output.Completion.IsCompleted;

                var inputBuffer = _output.BeginRead();
                var consumed = inputBuffer.Start;

                try
                {
                    if (inputBuffer.IsEmpty && fin)
                    {
                        return;
                    }

                    var data = inputBuffer.Slice(0, remaining);

                    foreach (var span in data)
                    {
                        await stream.WriteAsync(span.Array, span.Offset, span.Length);
                    }

                    consumed = data.End;
                    remaining -= data.Length;
                }
                finally
                {
                    _output.EndRead(consumed);
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
