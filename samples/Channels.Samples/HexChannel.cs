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
            const string hex = "0123456789ABCDEF";
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
                var writeIter = _channel.BeginWrite();
                for (int i = 0; i < data.Count; i++)
                {
                    byte b = data.Array[data.Offset + i];
                    writeIter.Write((byte)hex[(b >> 4)]);
                    writeIter.Write((byte)hex[(b & 0xf)]);
                    writeIter.Write((byte)' ');
                }

                inner.EndRead(span.End);

                await _channel.EndWriteAsync(writeIter);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();
        }
    }
}
