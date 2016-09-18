using System;
using System.Collections;
using System.Collections.Generic;
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

        /// <summary>
        /// Split a buffer into a sequence of tokens using a delimiter
        /// </summary>
        public static SplitEnumerable Split(this ReadableBuffer buffer, byte delimiter)
            => new SplitEnumerable(buffer, delimiter);

        /// <summary>
        /// Exposes the enumerator, which supports a simple iteration over a collection of a specified type.
        /// </summary>
        public struct SplitEnumerable : IEnumerable<ReadableBuffer>
        {
            /// <summary>
            /// Count the number of elemnts in this sequence
            /// </summary>
            public int Count()
            {
                if (_count >= 0) return _count;

                int count = 1;
                var current = _buffer;
                ReadableBuffer ignore;
                ReadCursor cursor;
                while(current.TrySliceTo(_delimiter, out ignore, out cursor))
                {
                    current = current.Slice(cursor).Slice(1);
                    count++;
                }
                return _count = count;
            }
            private ReadableBuffer _buffer;
            private byte _delimiter;
            private int _count;

            internal SplitEnumerable(ReadableBuffer buffer, byte delimiter)
            {
                _buffer = buffer;
                _delimiter = delimiter;
                _count = buffer.IsEmpty ? 0 : -1;
            }
            /// <summary>
            ///  Returns an enumerator that iterates through the collection.
            /// </summary>
            public SplitEnumerator GetEnumerator()
                => new SplitEnumerator(_buffer, _delimiter);

            IEnumerator<ReadableBuffer> IEnumerable<ReadableBuffer>.GetEnumerator()
                => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();
        }
        /// <summary>
        /// Supports a simple iteration over a sequence of buffers from a Split operation.
        /// </summary>
        public struct SplitEnumerator : IEnumerator<ReadableBuffer>
        {
            private ReadableBuffer _current, _remainder;
            private readonly byte _delimiter;

            internal SplitEnumerator(ReadableBuffer _remainder, byte _delimiter)
            {
                this._current = default(ReadableBuffer);
                this._remainder = _remainder;
                this._delimiter = _delimiter;
            }

            /// <summary>
            /// Gets the element in the collection at the current position of the enumerator.
            /// </summary>
            public ReadableBuffer Current => _current;

            object IEnumerator.Current => _current;

            /// <summary>
            /// Releases all resources owned by the instance
            /// </summary>
            public void Dispose() { }

            /// <summary>
            /// Advances the enumerator to the next element of the collection.
            /// </summary>
            public bool MoveNext()
            {
                ReadCursor cursor;
                if(_remainder.TrySliceTo(_delimiter, out _current, out cursor))
                {
                    _remainder = _remainder.Slice(cursor).Slice(1);
                    return true;
                }
                // once we're out of splits, yield whatever is left
                if(_remainder.IsEmpty)
                {
                    return false;
                }
                _current = _remainder;
                _remainder = default(ReadableBuffer);
                return true;
            }

            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }
        }
    }
}
