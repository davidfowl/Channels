using System;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Channels.Compression
{
    public static class CompressionChannelFactoryExtensions
    {
        public static IReadableChannel DeflateDecompress(this IReadableChannel channel, ChannelFactory factory)
        {
            var inflater = new ReadableDeflateChannel(ZLibNative.Deflate_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, inflater.Execute);
        }

        public static IReadableChannel DeflateCompress(this IReadableChannel channel, ChannelFactory factory, CompressionLevel compressionLevel)
        {
            var deflater = new WritableDeflateChannel(compressionLevel, ZLibNative.Deflate_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, deflater.Execute);
        }

        public static IReadableChannel GZipDecompress(this IReadableChannel channel, ChannelFactory factory)
        {
            var inflater = new ReadableDeflateChannel(ZLibNative.GZip_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, inflater.Execute);
        }

        public static IWritableChannel GZipCompress(this IWritableChannel channel, ChannelFactory factory, CompressionLevel compressionLevel)
        {
            var deflater = new WritableDeflateChannel(compressionLevel, ZLibNative.GZip_DefaultWindowBits);
            return factory.MakeWriteableChannel(channel, deflater.Execute);
        }

        public static IReadableChannel GZipCompress(this IReadableChannel channel, ChannelFactory factory, CompressionLevel compressionLevel)
        {
            var deflater = new WritableDeflateChannel(compressionLevel, ZLibNative.GZip_DefaultWindowBits);
            return factory.MakeReadableChannel(channel, deflater.Execute);
        }

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
                    var result = await input.ReadAsync();
                    var inputBuffer = result.Buffer;

                    if (inputBuffer.IsEmpty)
                    {
                        if (result.IsCompleted)
                        {
                            break;
                        }

                        input.Advance(inputBuffer.End);
                        continue;
                    }

                    var writerBuffer = output.Alloc();
                    var memory = inputBuffer.First;

                    unsafe
                    {
                        // TODO: Pin pointer if not pinned
                        void* inPointer;
                        if (memory.TryGetPointer(out inPointer))
                        {
                            _deflater.SetInput((IntPtr)inPointer, memory.Length);
                        }
                        else
                        {
                            throw new InvalidOperationException("Pointer needs to be pinned");
                        }
                    }

                    while (!_deflater.NeedsInput())
                    {
                        unsafe
                        {
                            void* outPointer;
                            writerBuffer.Ensure();
                            if (writerBuffer.Memory.TryGetPointer(out outPointer))
                            {
                                int written = _deflater.ReadDeflateOutput((IntPtr)outPointer, writerBuffer.Memory.Length);
                                writerBuffer.Advance(written);
                            }
                            else
                            {
                                throw new InvalidOperationException("Pointer needs to be pinned");
                            }
                        }
                    }

                    var consumed = memory.Length - _deflater.AvailableInput;

                    inputBuffer = inputBuffer.Slice(0, consumed);

                    input.Advance(inputBuffer.End);

                    await writerBuffer.FlushAsync();
                }

                bool flushed = false;
                do
                {
                    // Need to do more stuff here
                    var writerBuffer = output.Alloc();

                    unsafe
                    {
                        void* pointer;
                        writerBuffer.Ensure();
                        var memory = writerBuffer.Memory;
                        if (memory.TryGetPointer(out pointer))
                        {
                            int compressedBytes;
                            flushed = _deflater.Flush((IntPtr)pointer, memory.Length, out compressedBytes);
                            writerBuffer.Advance(compressedBytes);
                        }
                        else
                        {
                            throw new InvalidOperationException("Pointer needs to be pinned");
                        }
                    }

                    await writerBuffer.FlushAsync();
                }
                while (flushed);

                bool finished = false;
                do
                {
                    // Need to do more stuff here
                    var writerBuffer = output.Alloc();

                    unsafe
                    {
                        void* pointer;
                        writerBuffer.Ensure();
                        var memory = writerBuffer.Memory;
                        if (memory.TryGetPointer(out pointer))
                        {
                            int compressedBytes;
                            finished = _deflater.Finish((IntPtr)pointer, memory.Length, out compressedBytes);
                            writerBuffer.Advance(compressedBytes);
                        }
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
                    var result = await input.ReadAsync();
                    var inputBuffer = result.Buffer;

                    if (inputBuffer.IsEmpty)
                    {
                        if (result.IsCompleted)
                        {
                            break;
                        }

                        input.Advance(inputBuffer.End);
                        continue;
                    }

                    var writerBuffer = output.Alloc();
                    var memory = inputBuffer.First;
                    if (memory.Length > 0)
                    {
                        unsafe
                        {
                            void* pointer;
                            if (memory.TryGetPointer(out pointer))
                            {
                                _inflater.SetInput((IntPtr)pointer, memory.Length);

                                void* writePointer;
                                writerBuffer.Ensure();
                                if (writerBuffer.Memory.TryGetPointer(out writePointer))
                                {
                                    int written = _inflater.Inflate((IntPtr)writePointer, writerBuffer.Memory.Length);
                                    writerBuffer.Advance(written);
                                }
                                else
                                {
                                    throw new InvalidOperationException("Pointer needs to be pinned");
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("Pointer needs to be pinned");
                            }

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
