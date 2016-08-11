using System;
using System.Collections.Generic;
using System.IO;
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

                var readBuffer = inner.BeginRead();

                if (readBuffer.IsEnd && inner.Completion.IsCompleted)
                {
                    break;
                }

                var writeBuffer = _channel.BeginWrite();

                ArraySegment<byte> data;
                while (readBuffer.TryGetBuffer(out data))
                {
                    for (int i = 0; i < data.Count; i++)
                    {
                        byte b = data.Array[data.Offset + i];
                        writeBuffer.Write((byte)hex[(b >> 4)]);
                        writeBuffer.Write((byte)hex[(b & 0xf)]);
                        writeBuffer.Write((byte)' ');
                    }
                }

                inner.EndRead(readBuffer);

                await _channel.EndWriteAsync(writeBuffer);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();
        }
    }
}
