using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Samples.IO.Compression
{
    public static class CompressionChannelFactoryExtensions
    {
        public static IReadableChannel CreateDeflateDecompressChannel(this ChannelFactory factory, IReadableChannel channel)
        {
            var inflater = new ReadableDeflateChannel(ZLibNative.Deflate_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, inflater.Execute);
        }

        public static IReadableChannel CreateDeflateCompressChannel(this ChannelFactory factory, IReadableChannel channel, CompressionLevel compressionLevel)
        {
            var deflater = new WritableDeflateChannel(compressionLevel, ZLibNative.Deflate_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, deflater.Execute);
        }

        public static IReadableChannel CreateGZipDecompressChannel(this ChannelFactory factory, IReadableChannel channel)
        {
            var inflater = new ReadableDeflateChannel(ZLibNative.GZip_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, inflater.Execute);
        }

        public static IWritableChannel CreateGZipCompressChannel(this ChannelFactory factory, IWritableChannel channel, CompressionLevel compressionLevel)
        {
            var deflater = new WritableDeflateChannel(compressionLevel, ZLibNative.GZip_DefaultWindowBits);
            return factory.MakeWriteableChannel(channel, deflater.Execute);
        }

        public static IReadableChannel CreateGZipCompressChannel(this ChannelFactory factory, IReadableChannel channel, CompressionLevel compressionLevel)
        {
            var deflater = new WritableDeflateChannel(compressionLevel, ZLibNative.GZip_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, deflater.Execute);
        }

        private class WritableDeflateChannel
        {
            private readonly Deflater _deflater;

            public WritableDeflateChannel(CompressionLevel compressionLevel, int bits)
            {
                _deflater = new Deflater(compressionLevel, bits);
            }

            public async Task Execute(IReadableChannel input, IWritableChannel output)
            {
                while (true)
                {
                    await input;

                    var inputBuffer = input.BeginRead();

                    if (inputBuffer.IsEmpty && input.Completion.IsCompleted)
                    {
                        break;
                    }

                    var writerBuffer = output.Alloc(2048);
                    var span = inputBuffer.FirstSpan;

                    _deflater.SetInput(span.BufferPtr, span.Length);

                    while (!_deflater.NeedsInput())
                    {
                        int written = _deflater.ReadDeflateOutput(writerBuffer.Memory.BufferPtr, writerBuffer.Memory.Length);
                        writerBuffer.UpdateWritten(written);
                    }

                    var consumed = span.Length - _deflater.AvailableInput;

                    inputBuffer = inputBuffer.Slice(0, consumed);

                    input.EndRead(inputBuffer);

                    await output.WriteAsync(writerBuffer);
                }

                bool flushed;
                do
                {
                    // Need to do more stuff here
                    var writerBuffer = output.Alloc(2048);

                    int compressedBytes;
                    flushed = _deflater.Flush(writerBuffer.Memory.BufferPtr, writerBuffer.Memory.Length, out compressedBytes);
                    writerBuffer.UpdateWritten(compressedBytes);

                    await output.WriteAsync(writerBuffer);
                }
                while (flushed);

                bool finished;
                do
                {
                    // Need to do more stuff here
                    var writerBuffer = output.Alloc(2048);

                    int compressedBytes;
                    finished = _deflater.Finish(writerBuffer.Memory.BufferPtr, writerBuffer.Memory.Length, out compressedBytes);
                    writerBuffer.UpdateWritten(compressedBytes);

                    await output.WriteAsync(writerBuffer);
                }
                while (!finished);

                input.CompleteReading();

                output.CompleteWriting();

                _deflater.Dispose();
            }
        }

        private class ReadableDeflateChannel
        {
            private readonly Inflater _inflater;

            public ReadableDeflateChannel(int bits)
            {
                _inflater = new Inflater(bits);
            }

            public async Task Execute(IReadableChannel input, IWritableChannel output)
            {
                while (true)
                {
                    await input;

                    var inputBuffer = input.BeginRead();

                    if (inputBuffer.IsEmpty && input.Completion.IsCompleted)
                    {
                        break;
                    }

                    var writerBuffer = output.Alloc(2048);
                    var span = inputBuffer.FirstSpan;
                    if (span.Length > 0)
                    {
                        _inflater.SetInput(span.BufferPtr, span.Length);

                        int written = _inflater.Inflate(writerBuffer.Memory.BufferPtr, writerBuffer.Memory.Length);

                        writerBuffer.UpdateWritten(written);

                        var consumed = span.Length - _inflater.AvailableInput;

                        inputBuffer = inputBuffer.Slice(0, consumed);
                    }

                    input.EndRead(inputBuffer);

                    await output.WriteAsync(writerBuffer);
                }

                input.CompleteReading();

                output.CompleteWriting();

                _inflater.Dispose();
            }
        }
    }
}
