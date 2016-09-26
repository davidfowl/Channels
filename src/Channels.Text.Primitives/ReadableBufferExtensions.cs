﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Utf8;

namespace Channels.Text.Primitives
{
    /// <summary>
    /// Extension methods 
    /// </summary>
    public static class ReadableBufferExtensions
    {
        /// <summary>
        /// Trim whitespace starting from the specified <see cref="ReadableBuffer"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="ReadableBuffer"/> to trim</param>
        /// <returns>A new <see cref="ReadableBuffer"/> with the starting whitespace trimmed.</returns>
        public static ReadableBuffer TrimStart(this ReadableBuffer buffer)
        {
            int start = 0;
            foreach (var memory in buffer)
            {
                var span = memory.Span;
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

        /// <summary>
        /// Parses a <see cref="uint"/> from the specified <see cref="ReadableBuffer"/>
        /// </summary>
        /// <param name="buffer">The <see cref="ReadableBuffer"/> to parse</param>
        public unsafe static uint GetUInt32(this ReadableBuffer buffer)
        {
            ReadOnlySpan<byte> textSpan;

            if (buffer.IsSingleSpan)
            {
                // It fits!
                textSpan = buffer.First.Span;
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
            if (!PrimitiveParser.TryParse(utf8Buffer, out value))
            {
                throw new InvalidOperationException();
            }
            return value;
        }

        /// <summary>
        /// Parses a <see cref="ulong"/> from the specified <see cref="ReadableBuffer"/>
        /// </summary>
        /// <param name="buffer">The <see cref="ReadableBuffer"/> to parse</param>
        public unsafe static ulong GetUInt64(this ReadableBuffer buffer)
        {
            byte* addr;
            ulong value;
            int consumed, len = buffer.Length;
            if (buffer.IsSingleSpan)
            {
                // It fits!
                addr = (byte*)buffer.First.UnsafePointer;
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
                if (!PrimitiveParser.TryParse(arr, 0, EncodingData.InvariantUtf8, Format.Parsed.HexUppercase, out value, out consumed))
                {
                    throw new InvalidOperationException();
                }
                return value;
            }

            if (!PrimitiveParser.TryParse(addr, 0, len, EncodingData.InvariantUtf8, Format.Parsed.HexUppercase, out value, out consumed))
            {
                throw new InvalidOperationException();
            }
            return value;
        }

        /// <summary>
        /// Decodes the ASCII encoded bytes in the <see cref="ReadableBuffer"/> into a <see cref="string"/>
        /// </summary>
        /// <param name="buffer">The buffer to decode</param>
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

                foreach (var memory in buffer)
                {
                    if (!AsciiUtilities.TryGetAsciiString((byte*)memory.UnsafePointer, output + offset, memory.Length))
                    {
                        throw new InvalidOperationException();
                    }

                    offset += memory.Length;
                }
            }

            return asciiString;
        }

        /// <summary>
        /// Decodes the utf8 encoded bytes in the <see cref="ReadableBuffer"/> into a <see cref="string"/>
        /// </summary>
        /// <param name="buffer">The buffer to decode</param>
        public static unsafe string GetUtf8String(this ReadableBuffer buffer)
        {
            if (buffer.IsEmpty)
            {
                return null;
            }

            ReadOnlySpan<byte> textSpan;

            if (buffer.IsSingleSpan)
            {
                textSpan = buffer.First.Span;
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

        /// <summary>
        /// Split a buffer into a sequence of tokens using a delimiter
        /// </summary>
        public static SplitEnumerable Split(this ReadableBuffer buffer, byte delimiter)
            => new SplitEnumerable(buffer, delimiter);
    }
}
