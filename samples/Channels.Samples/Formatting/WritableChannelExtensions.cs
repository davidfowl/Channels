using System;
using System.Text;
using System.Text.Formatting;

namespace Channels.Samples.Formatting
{
    public static class WritableChannelExtensions
    {
        public static WriteableBufferFormatter GetFormatter(this IWritableChannel channel, EncodingData formattingData)
        {
            var buffer = channel.Alloc(2048);
            return new WriteableBufferFormatter(ref buffer, formattingData);
        }
    }

    public class WriteableBufferFormatter : IFormatter
    {
        private WritableBuffer _writableBuffer;

        public WriteableBufferFormatter(ref WritableBuffer writableBuffer, EncodingData formattingData)
        {
            _writableBuffer = writableBuffer;
            Encoding = formattingData;
        }

        public WritableBuffer Buffer => _writableBuffer;

        public EncodingData Encoding { get; }

        public Span<byte> FreeBuffer
        {
            get
            {
                return _writableBuffer.Memory;
            }
        }

        public void CommitBytes(int bytes)
        {
            _writableBuffer.CommitBytes(bytes);
        }

        public void ResizeBuffer(int desiredFreeBytesHint = -1)
        {
            _writableBuffer.Ensure(desiredFreeBytesHint == -1 ? 2048 : desiredFreeBytesHint);
        }
    }
}
