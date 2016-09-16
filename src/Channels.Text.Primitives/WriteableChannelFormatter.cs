using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Formatting;
using System.Text;

namespace Channels.Text.Primitives
{
    public class WriteableChannelFormatter : IFormatter
    {
        private readonly IWritableChannel _channel;
        private WritableBuffer _writableBuffer;

        public WriteableChannelFormatter(IWritableChannel channel, EncodingData encoding)
        {
            _channel = channel;
            Encoding = encoding;
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
