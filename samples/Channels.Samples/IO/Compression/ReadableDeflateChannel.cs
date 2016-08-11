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

                var readBuffer = inner.BeginRead();

                if (readBuffer.IsEnd && inner.Completion.IsCompleted)
                {
                    break;
                }

                var writerBuffer = _channel.BeginWrite(2048);

                int readCount = readBuffer.Memory.Length;

                _inflater.SetInput(readBuffer.Memory.BufferPtr, readCount);

                int written = _inflater.Inflate(writerBuffer.Memory.BufferPtr, writerBuffer.Memory.Length);

                writerBuffer.UpdateWritten(written);

                // Move the read iterator
                readBuffer.Seek(readCount - _inflater.AvailableInput);

                if (readCount == 0)
                {
                    // Move next
                    readBuffer.Seek();
                }

                inner.EndRead(readBuffer);

                await _channel.EndWriteAsync(writerBuffer);
            }

            inner.CompleteReading();

            _channel.CompleteWriting();

            _inflater.Dispose();
        }
    }
}
