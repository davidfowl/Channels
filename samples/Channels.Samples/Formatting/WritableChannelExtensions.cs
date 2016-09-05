using System;
using System.Text;
using System.Text.Formatting;

namespace Channels.Samples.Formatting
{
    public static class WritableChannelExtensions
    {
        public static WriteableBufferFormatter GetFormatter(this IWritableChannel channel, FormattingData formattingData)
        {
            var buffer = channel.Alloc(2048);
            return new WriteableBufferFormatter(ref buffer, formattingData);
        }
    }

    public class WriteableBufferFormatter : IFormatter
    {
        private WritableBuffer _writableBuffer;

        public WriteableBufferFormatter(ref WritableBuffer writableBuffer, FormattingData formattingData)
        {
            _writableBuffer = writableBuffer;
            FormattingData = formattingData;
        }

        public WritableBuffer Buffer => _writableBuffer;

        public FormattingData FormattingData { get; }

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
