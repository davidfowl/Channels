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
                    var inputBuffer = await input.ReadAsync();

                    if (inputBuffer.IsEmpty && input.Reading.IsCompleted)
                    {
                        break;
                    }

                    var writerBuffer = output.Alloc(2048);
                    var memory = inputBuffer.First;

                    unsafe
                    {
                        _deflater.SetInput((IntPtr)memory.UnsafePointer, memory.Length);
                    }

                    while (!_deflater.NeedsInput())
                    {
                        unsafe
                        {
                            int written = _deflater.ReadDeflateOutput((IntPtr)writerBuffer.RawMemory.UnsafePointer, writerBuffer.Memory.Length);
                            writerBuffer.Advance(written);
                        }
                    }

                    var consumed = memory.Length - _deflater.AvailableInput;

                    inputBuffer = inputBuffer.Slice(0, consumed);

                    input.Advance(inputBuffer.End);

                    await writerBuffer.FlushAsync();
                }

                bool flushed;
                do
                {
                    // Need to do more stuff here
                    var writerBuffer = output.Alloc(2048);
                    var memory = writerBuffer.RawMemory;

                    unsafe
                    {
                        int compressedBytes;
                        flushed = _deflater.Flush((IntPtr)memory.UnsafePointer, memory.Length, out compressedBytes);
                        writerBuffer.Advance(compressedBytes);
                    }

                    await writerBuffer.FlushAsync();
                }
                while (flushed);

                bool finished;
                do
                {
                    // Need to do more stuff here
                    var writerBuffer = output.Alloc(2048);
                    var memory = writerBuffer.RawMemory;

                    unsafe
                    {
                        int compressedBytes;
                        finished = _deflater.Finish((IntPtr)memory.UnsafePointer, memory.Length, out compressedBytes);
                        writerBuffer.Advance(compressedBytes);
                    }

                    await writerBuffer.FlushAsync();
                }
                while (!finished);

                input.Complete();

                output.Complete();

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
                    var inputBuffer = await input.ReadAsync();

                    if (inputBuffer.IsEmpty && input.Reading.IsCompleted)
                    {
                        break;
                    }

                    var writerBuffer = output.Alloc(2048);
                    var memory = inputBuffer.First;
                    if (memory.Length > 0)
                    {
                        unsafe
                        {
                            _inflater.SetInput((IntPtr)memory.UnsafePointer, memory.Length);

                            int written = _inflater.Inflate((IntPtr)memory.UnsafePointer, memory.Length);

                            writerBuffer.Advance(written);

                            var consumed = memory.Length - _inflater.AvailableInput;

                            inputBuffer = inputBuffer.Slice(0, consumed);
                        }
                    }

                    input.Advance(inputBuffer.End);

                    await writerBuffer.FlushAsync();
                }

                input.Complete();

                output.Complete();

                _inflater.Dispose();
            }
        }
    }
}
