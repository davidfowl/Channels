using System;
using System.Text;
using System.Text.Formatting;
using System.Threading.Tasks;

namespace Channels.Samples.Formatting
{
    public static class WritableChannelExtensions
    {
        public static WriteableChannelFormatter GetFormatter(this IWritableChannel channel, EncodingData formattingData)
        {
            return new WriteableChannelFormatter(channel, formattingData);
        }
    }

    public class WriteableChannelFormatter : IFormatter
    {
        private IWritableChannel _channel;
        private WritableBuffer _writableBuffer;

        public WriteableChannelFormatter(IWritableChannel channel, EncodingData formattingData)
        {
            _channel = channel;
            Encoding = formattingData;
            _writableBuffer = _channel.Alloc();
        }

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

        public async Task FlushAsync()
        {
            await _writableBuffer.FlushAsync();
            _writableBuffer = _channel.Alloc();
        }
    }
}
