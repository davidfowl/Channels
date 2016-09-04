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

        public unsafe static uint GetUInt32(this ReadableBuffer buffer)
        {
            var textSpan = default(ReadOnlySpan<byte>);

            if (buffer.IsSingleSpan)
            {
                // It fits!
                var span = buffer.FirstSpan;

                // Is there a better way to do this?
                textSpan = new ReadOnlySpan<byte>(span.UnsafePointer, span.Length);
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
                    if (!AsciiUtilities.TryGetAsciiString((byte*)span.UnsafePointer, output + offset, span.Length))
                    {
                        throw new InvalidOperationException();
                    }

                    offset += span.Length;
                }
            }

            return asciiString;
        }

        public static unsafe string GetUtf8String(this ReadableBuffer buffer)
        {
            if (buffer.IsEmpty)
            {
                return null;
            }

            if (buffer.IsSingleSpan)
            {
                var span = buffer.FirstSpan;
                return new Utf8String(span).ToString();
            }
            else if (buffer.Length < 128) // REVIEW: What's a good number
            {
                var target = stackalloc byte[128];

                buffer.CopyTo(target, length: 128);

                return new Utf8String(new ReadOnlySpan<byte>(target, buffer.Length)).ToString();
            }
            else
            {
                // Heap allocated copy to parse into array (should be rare)
                return new Utf8String(buffer.ToArray()).ToString();
            }
        }
    }
}
