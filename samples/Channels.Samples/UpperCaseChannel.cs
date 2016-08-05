using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Samples
{
    public class UpperCaseChannel : ReadableChannel
    {
        public UpperCaseChannel(IReadableChannel inner, MemoryPool pool) : base(pool)
        {
            Process(inner);
        }

        private async void Process(IReadableChannel inner)
        {
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
                    if (b >= 'a' && b <= 'z')
                    {
                        // To upper
                        iter.Write((byte)(b & 0xdf));
                    }
                    else
                    {
                        // Leave it alone
                        iter.Write(b);
                    }
                }

                await _channel.EndWriteAsync(iter);

                inner.EndRead(span.End);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();
        }
    }
}
