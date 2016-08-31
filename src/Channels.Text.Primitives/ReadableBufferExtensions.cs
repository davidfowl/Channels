using System;
using System.Text;
using System.Text.Utf8;
using System.Threading;

namespace Channels.Text.Primitives
{
    public static class ReadableBufferExtensions
    {
        private static readonly Encoding Utf8Encoding = Encoding.UTF8;
        private static Decoder Utf8Decoder;

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

        public unsafe static uint GetInt32(this ReadableBuffer buffer)
        {
            var textSpan = default(ReadOnlySpan<byte>);

            if (buffer.IsSingleSpan)
            {
                // It fits!
                textSpan = new ReadOnlySpan<byte>(buffer.FirstSpan.Array, buffer.FirstSpan.Offset, buffer.FirstSpan.Length);
            }
            else if (buffer.Length < 128) // REVIEW: What's a good number
            {
                var target = stackalloc byte[128];

                buffer.CopyTo(target, length: 128);

                textSpan = new ReadOnlySpan<byte>(target, buffer.Length);
            }
            else
            {
                // Heap allocated copy to parse into array (should be rare)
                textSpan = new ReadOnlySpan<byte>(buffer.ToArray());
            }

            uint value;
            var utf8Buffer = new Utf8String(textSpan);
            if (!InvariantParser.TryParse(utf8Buffer, out value))
            {
                throw new InvalidOperationException();
            }
            return value;
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
                return Utf8Encoding.GetString(buffer.FirstSpan.Array, buffer.FirstSpan.Offset, buffer.FirstSpan.Length);
            }
            // try to re-use a shared decoder; note that in heavy usage, we might need to allocate another
            var decoder = Interlocked.Exchange(ref Utf8Decoder, null);
            if (decoder == null) decoder = Utf8Encoding.GetDecoder();
            else decoder.Reset();

            var length = buffer.Length;
            var charLength = length; // worst case is 1 byte per char
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
                    false, // a single character could span two spans
                    out bytesUsed,
                    out charsUsed,
                    out completed);

                charIndex += charsUsed;
            }
            // make the decoder available for re-use
            Interlocked.CompareExchange(ref Utf8Decoder, decoder, null);
            return new string(chars, 0, charIndex);
        }
    }
}
