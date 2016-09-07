using System.Collections.Generic;
using System.Numerics;
using Channels.Text.Primitives;
using Microsoft.Extensions.Primitives;

namespace Channels.Samples.Http
{
    public class FormReader
    {
        private static Vector<byte> _vectorAnd = new Vector<byte>((byte)'&');
        private static Vector<byte> _vectorEq = new Vector<byte>((byte)'=');

        private Dictionary<string, StringValues> _data = new Dictionary<string, StringValues>();
        private long? _contentLength;

        public FormReader(long? contentLength)
        {
            _contentLength = contentLength;
        }

        public Dictionary<string, StringValues> FormValues => _data;

        public bool TryParse(ref ReadableBuffer buffer)
        {
            if (buffer.IsEmpty || !_contentLength.HasValue)
            {
                return true;
            }

            while (!buffer.IsEmpty && _contentLength > 0)
            {
                var next = buffer;
                var delim = next.IndexOf(ref _vectorEq);

                if (delim == ReadCursor.NotFound)
                {
                    break;
                }

                var key = next.Slice(0, delim);
                next = next.Slice(delim).Slice(1);

                var value = default(ReadableBuffer);

                delim = next.IndexOf(ref _vectorAnd);

                if (delim == ReadCursor.NotFound)
                {
                    var remaining = _contentLength - buffer.Length;

                    if (remaining == 0)
                    {
                        value = next;
                        next = next.Slice(next.End);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    value = next.Slice(0, delim);
                    next = next.Slice(delim).Slice(1);
                }

                // TODO: Combine multi value keys
                _data[key.GetUtf8String()] = value.GetUtf8String();
                _contentLength -= (buffer.Length - next.Length);
                buffer = next;
            }

            return _contentLength == 0;
        }
    }
}
