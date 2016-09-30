using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public static class StreamExtensions
    {
        /// <summary>
        /// Adapts a <see cref="Stream"/> into a <see cref="IReadableChannel"/>.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static IReadableChannel AsReadableChannel(this Stream stream) => AsReadableChannel(stream, CancellationToken.None);

        /// <summary>
        /// Adapts a <see cref="Stream"/> into a <see cref="IReadableChannel"/>.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static IReadableChannel AsReadableChannel(this Stream stream, CancellationToken cancellationToken)
        {
            var streamAdaptor = new UnownedBufferStream(stream);
            streamAdaptor.Produce(cancellationToken);
            return streamAdaptor.Channel;
        }

        /// <summary>
        /// Copies the content of a <see cref="Stream"/> into a <see cref="IWritableChannel"/>.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="channel"></param>
        /// <returns></returns>
        public static Task CopyToAsync(this Stream stream, IWritableChannel channel)
        {
            return stream.CopyToAsync(new StreamChannel(channel));
        }

        private class UnownedBufferStream : Stream
        {
            private readonly Stream _stream;
            private readonly UnownedBufferChannel _channel;

            public IReadableChannel Channel => _channel;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;

            public override long Length
            {
                get
                {
                    throw new NotImplementedException();
                }
            }

            public override long Position
            {
                get
                {
                    throw new NotImplementedException();
                }

                set
                {
                    throw new NotImplementedException();
                }
            }

            public UnownedBufferStream(Stream stream)
            {
                _stream = stream;
                _channel = new UnownedBufferChannel();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                WriteAsync(buffer, offset, count).Wait();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _channel.WriteAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
            }

            // *gasp* Async Void!? It works here because we still have _channel.Writing to track completion.
            internal async void Produce(CancellationToken cancellationToken)
            {
                // Wait for a reader
                await _channel.ReadingStarted;

                try
                {
                    // We have to provide a buffer size in order to provide a cancellation token. Weird but meh.
                    // 4096 is the "default" value.
                    await _stream.CopyToAsync(this, 4096, cancellationToken);
                    _channel.CompleteWriter();
                }
                catch (Exception ex)
                {
                    _channel.CompleteWriter(ex);
                }
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }

        private class StreamChannel : Stream
        {
            private IWritableChannel _channel;

            public StreamChannel(IWritableChannel channel)
            {
                _channel = channel;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length
            {
                get
                {
                    throw new NotSupportedException();
                }
            }

            public override long Position
            {
                get
                {
                    throw new NotSupportedException();
                }

                set
                {
                    throw new NotSupportedException();
                }
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var channelBuffer = _channel.Alloc();
                channelBuffer.Write(new Span<byte>(buffer, offset, count));
                await channelBuffer.FlushAsync();
            }
        }
    }
}
