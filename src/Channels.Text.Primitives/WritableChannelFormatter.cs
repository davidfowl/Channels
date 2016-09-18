using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Formatting;
using System.Text;

namespace Channels.Text.Primitives
{
    public class WritableChannelFormatter : IFormatter
    {
        private readonly IWritableChannel _channel;
        private WritableBuffer _writableBuffer;
        private bool _needAlloc = true;

        public WritableChannelFormatter(IWritableChannel channel, EncodingData encoding)
        {
            _channel = channel;
            Encoding = encoding;
        }

        public EncodingData Encoding { get; }

        public Span<byte> FreeBuffer
        {
            get
            {
                EnsureBuffer();

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

        public void Write(Span<byte> data)
        {
            EnsureBuffer();
            _writableBuffer.Write(data);
        }

        public async Task FlushAsync()
        {
            await _writableBuffer.FlushAsync();
            _needAlloc = true;
        }

        private void EnsureBuffer()
        {
            if (_needAlloc)
            {
                _writableBuffer = _channel.Alloc();
                _needAlloc = false;
            }
        }
    }
}
