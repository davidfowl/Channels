using System;
using System.Text;
using System.Text.Utf8;

namespace Channels.Text.Primitives
{
    public static class ReadableBufferExtensions
    {
        public static ReadableBuffer TrimStart(this ReadableBuffer buffer)
        {
            int start = 0;
            foreach (var span in buffer)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    if (!IsWhitespaceChar(span[i]))
                    {
                        break;
                    }

                    start++;
                }
            }

            return buffer.Slice(start);
        }

        private static bool IsWhitespaceChar(int ch)
        {
            return ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r';
        }

        public unsafe static uint GetUInt32(this ReadableBuffer buffer)
        {
            ReadOnlySpan<byte> textSpan;

            if (buffer.IsSingleSpan)
            {
                // It fits!
                textSpan = buffer.FirstSpan;
            }
            else if (buffer.Length < 128) // REVIEW: What's a good number
            {
                var data = stackalloc byte[128];
                var destination = new Span<byte>(data, 128);

                buffer.CopyTo(destination);

                textSpan = destination.Slice(0, buffer.Length);
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
        public unsafe static ulong GetUInt64(this ReadableBuffer buffer)
        {
            byte* addr;
            ulong value;
            int consumed, len = buffer.Length;
            if (buffer.IsSingleSpan)
            {
                // It fits!
                addr = (byte*)buffer.FirstSpan.UnsafePointer;
            }
            else if (len < 128) // REVIEW: What's a good number
            {
                var data = stackalloc byte[len];
                buffer.CopyTo(new Span<byte>(data, len));
                addr = data; // memory allocated via stackalloc is valid and
                // intact until the end of the method; we don't need to worry about scope
            }
            else
            {
                // Heap allocated copy to parse into array (should be rare)
                var arr = buffer.ToArray();
                if (!InvariantParser.TryParse(arr, 0, FormattingData.InvariantUtf8, Format.Parsed.HexUppercase, out value, out consumed))
                {
                    throw new InvalidOperationException();
                }
                return value;
            }

            if (!InvariantParser.TryParse(addr, 0, len, FormattingData.InvariantUtf8, Format.Parsed.HexUppercase, out value, out consumed))
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

            ReadOnlySpan<byte> textSpan;

            if (buffer.IsSingleSpan)
            {
                textSpan = buffer.FirstSpan;
            }
            else if (buffer.Length < 128) // REVIEW: What's a good number
            {
                var data = stackalloc byte[128];
                var destination = new Span<byte>(data, 128);

                buffer.CopyTo(destination);

                textSpan = destination.Slice(0, buffer.Length);
            }
            else
            {
                // Heap allocated copy to parse into array (should be rare)
                textSpan = new ReadOnlySpan<byte>(buffer.ToArray());
            }

            return new Utf8String(textSpan).ToString();
        }
    }
}
