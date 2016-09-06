using System.Collections.Generic;
using System.Numerics;
using Channels.Text.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Channels.Samples.Http
{
    public class FormReader
    {
        private static Vector<byte> _vectorAnd = new Vector<byte>((byte)'&');
        private static Vector<byte> _vectorEq = new Vector<byte>((byte)'=');

        public static bool TryParse(ref ReadableBuffer buffer, ref Dictionary<string, StringValues> data, ref long? contentLength)
        {
            if (buffer.IsEmpty || !contentLength.HasValue)
            {
                return true;
            }

            while (!buffer.IsEmpty && contentLength > 0)
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
                    var remaining = contentLength - buffer.Length;

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
                data[key.GetUtf8String()] = value.GetUtf8String();
                contentLength -= (buffer.Length - next.Length);
                buffer = next;
            }

            return contentLength == 0;
        }
    }
}
