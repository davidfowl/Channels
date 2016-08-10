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

                int readCount = readIter.ReadableCount;

                _inflater.SetInput(readIter.ReadableDataArrayPtr, readCount);

                int written = _inflater.Inflate(writeIter.WritableDataArrayPtr, writeIter.WritableCount);

                writeIter.UpdateEnd(written);

                // Move the read iterator
                readIter.Seek(readCount - _inflater.AvailableInput);

                if (readCount == 0)
                {
                    // Hacky
                    readIter.Seek(1);
                }

                inner.EndRead(readIter);

                await _channel.EndWriteAsync(writeIter);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();

            _inflater.Dispose();
        }
    }
}
