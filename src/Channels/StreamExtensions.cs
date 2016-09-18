using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public static class StreamExtensions
    {
        public static async Task CopyToAsync(this Stream stream, IWritableChannel channel)
        {
            try
            {
                await stream.CopyToAsync(new StreamChannel(channel));
            }
            catch(Exception ex)
            {
                channel.Complete(ex);
            }
            finally
            {
                channel.Complete();
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
