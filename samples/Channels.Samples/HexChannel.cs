using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Samples
{
    public class HexChannel : ReadableChannel
    {
        public HexChannel(IReadableChannel inner, MemoryPool pool) : base(pool)
        {
            Process(inner);
        }

        private async void Process(IReadableChannel inner)
        {
            var hex = "0123456789ABCDEF";
            while (true)
            {
                await inner;

                var span = inner.BeginRead();

                if (span.Begin.IsEnd && inner.Completion.IsCompleted)
                {
                    break;
                }

                // PERF: This might copy
                var data = span.Begin.GetArraySegment(span.End);
                var iter = _channel.BeginWrite();
                for (int i = 0; i < data.Count; i++)
                {
                    byte b = data.Array[data.Offset + i];
                    iter.Write((byte)hex[(b >> 4)]);
                    iter.Write((byte)hex[(b & 0xf)]);
                    iter.Write((byte)' ');
                }

                await _channel.EndWriteAsync(iter);

                inner.EndRead(span.End);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();
        }
    }
}
