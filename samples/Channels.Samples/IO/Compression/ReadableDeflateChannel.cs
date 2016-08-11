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
                var end = readBuffer;

                BufferSpan span;
                if (!end.TryGetBuffer(out span) && inner.Completion.IsCompleted)
                {
                    break;
                }

                var writerBuffer = _channel.BeginWrite(2048);
 
                _inflater.SetInput(span.BufferPtr, span.Length);

                int written = _inflater.Inflate(writerBuffer.Memory.BufferPtr, writerBuffer.Memory.Length);

                writerBuffer.UpdateWritten(written);

                var consumed = span.Length - _inflater.AvailableInput;

                // Move the read iterator
                readBuffer.Seek(consumed);

                if (consumed == 0)
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
