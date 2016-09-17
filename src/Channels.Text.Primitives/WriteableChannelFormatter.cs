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
        private bool _needAlloc = true;

        public WriteableChannelFormatter(IWritableChannel channel, EncodingData encoding)
        {
            _channel = channel;
            Encoding = encoding;
        }

        public EncodingData Encoding { get; }

        public Span<byte> FreeBuffer
        {
            get
            {
                if (_needAlloc)
                {
                    _writableBuffer = _channel.Alloc();
                    _needAlloc = false;
                }

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
            _needAlloc = true;
        }
    }
}
