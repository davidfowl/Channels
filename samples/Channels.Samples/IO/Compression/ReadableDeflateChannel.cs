using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace Channels.Samples.IO.Compression
{
    public class ReadableDeflateChannel : ReadableChannel
    {
        private readonly Inflater _inflater;

        public ReadableDeflateChannel(IReadableChannel inner, MemoryPool pool) : this(inner, ZLibNative.Deflate_DefaultWindowBits, pool)
        {
        }

        public ReadableDeflateChannel(IReadableChannel inner, int bits, MemoryPool pool) : base(pool)
        {
            _inflater = new Inflater(bits);

            DoDeflate(inner);
        }

        private async void DoDeflate(IReadableChannel inner)
        {
            while (true)
            {
                await inner;

                var readIter = inner.BeginRead().Begin;

                if (readIter.IsEnd && inner.Completion.IsCompleted)
                {
                    break;
                }

                var writeIter = _channel.BeginWrite(2048);

                _inflater.SetInput(readIter.ReadableDataArrayPtr, readIter.ReadableCount);

                int read = _inflater.Inflate(writeIter.WritableDataArrayPtr, writeIter.WritableCount);

                writeIter.UpdateEnd(read);

                await _channel.EndWriteAsync(writeIter);

                readIter.Seek(read);

                inner.EndRead(readIter);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();

            _inflater.Dispose();
        }
    }
}
