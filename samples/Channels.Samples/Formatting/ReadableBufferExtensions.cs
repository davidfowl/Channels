using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Channels.Samples
{
    public static class ReadableBufferExtensions
    {
        public static ReadableBuffer TrimStart(this ReadableBuffer buffer)
        {
            int ch;
            while ((ch = buffer.Peek()) != -1)
            {
                if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                {
                    buffer = buffer.Slice(1);
                }
                else
                {
                    break;
                }
            }

            return buffer;
        }

        public unsafe static string GetAsciiString(this ReadableBuffer buffer)
        {
            if (buffer.IsEmpty)
            {
                return null;
            }

            var asciiString = new string('\0', buffer.Length);

            fixed (char* outputStart = asciiString)
            {
                int offset = 0;
                var output = outputStart;

                foreach (var span in buffer)
                {
                    if (!AsciiUtilities.TryGetAsciiString((byte*)span.BufferPtr, output + offset, span.Length))
                    {
                        throw new InvalidOperationException();
                    }

                    offset += span.Length;
                }
            }

            return asciiString;
        }

        public static string GetUtf8String(this ReadableBuffer buffer)
        {
            if (buffer.IsEmpty)
            {
                return null;
            }

            if (buffer.IsSingleSpan)
            {
                return Encoding.UTF8.GetString(buffer.FirstSpan.Array, buffer.FirstSpan.Offset, buffer.FirstSpan.Length);
            }

            var decoder = Encoding.UTF8.GetDecoder();

            var length = buffer.Length;
            var charLength = length * 2;
            var chars = new char[charLength];
            var charIndex = 0;

            int bytesUsed = 0;
            int charsUsed = 0;
            bool completed;

            foreach (var span in buffer)
            {
                decoder.Convert(
                    span.Array,
                    span.Offset,
                    span.Length,
                    chars,
                    charIndex,
                    charLength - charIndex,
                    true,
                    out bytesUsed,
                    out charsUsed,
                    out completed);

                charIndex += charsUsed;
            }

            return new string(chars, 0, charIndex);
        }
    }
}
