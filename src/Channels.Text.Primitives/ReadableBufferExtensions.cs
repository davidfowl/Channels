﻿using System;
using System.Text;
using System.Text.Utf8;

namespace Channels.Text.Primitives
{
    public static class ReadableBufferExtensions
    {
        public static ReadableBuffer TrimStart(this ReadableBuffer buffer)
        {
            var enumerator = buffer.GetEnumerator();

            if (!enumerator.MoveNext())
            {
                return default(ReadableBuffer);
            }

            var span = enumerator.Current;
            var start = 0;
            var complete = false;

            for (var i = 0; i < span.Length; i++)
            {
                if (!IsWhitespaceChar(span[i]))
                {
                    complete = true;
                    break;
                }

                start++;
            }

            if (complete || !enumerator.MoveNext())
            {
                return start == 0 ? buffer : buffer.Slice(start);
            }

            start = TrimStartMultiSpan(ref enumerator, start);

            return buffer.Slice(start);
        }

        private static int TrimStartMultiSpan(ref ReadableBuffer.Enumerator enumerator, int start)
        {
            do
            {
                var span = enumerator.Current;
                for (var i = 0; i < span.Length; i++)
                {
                    if (!IsWhitespaceChar(span[i]))
                    {
                        break;
                    }
                    start++;
                }
            } while (enumerator.MoveNext());

            return start;
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
                var target = stackalloc byte[len];
                buffer.CopyTo(target, len);
                addr = target; // memory allocated via stackalloc is valid and
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
                var target = stackalloc byte[128];

                buffer.CopyTo(target, length: 128);

                textSpan = new ReadOnlySpan<byte>(target, buffer.Length);
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
