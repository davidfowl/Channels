// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Channels
{
    public static class MemoryPoolIteratorExtensions
    {
        private static readonly Encoding _utf8 = Encoding.UTF8;

        public static async Task CopyToAsync(this MemoryPoolIterator start, Stream stream, MemoryPoolBlock end)
        {
            if (start.IsDefault)
            {
                return;
            }

            var block = start.Block;
            var index = start.Index;

            while (true)
            {
                // Determine if we might attempt to copy data from block.Next before
                // calculating "following" so we don't risk skipping data that could
                // be added after block.End when we decide to copy from block.Next.
                // block.End will always be advanced before block.Next is set.
                var wasLastBlock = block.Next == null || block == end;
                var following = block.End - index;
                if (wasLastBlock)
                {
                    await stream.WriteAsync(block.Array, index, following);
                    break;
                }
                else
                {
                    await stream.WriteAsync(block.Array, index, following);
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public unsafe static string GetAsciiString(this MemoryPoolIterator start, MemoryPoolIterator end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return null;
            }

            var length = start.GetLength(end);

            if (length == 0)
            {
                return null;
            }

            // Bytes out of the range of ascii are treated as "opaque data"
            // and kept in string as a char value that casts to same input byte value
            // https://tools.ietf.org/html/rfc7230#section-3.2.4

            var inputOffset = start.Index;
            var block = start.Block;

            var asciiString = new string('\0', length);

            fixed (char* outputStart = asciiString)
            {
                var output = outputStart;
                var remaining = length;

                var endBlock = end.Block;
                var endIndex = end.Index;

                while (true)
                {
                    int following = (block != endBlock ? block.End : endIndex) - inputOffset;

                    if (following > 0)
                    {
                        var input = block.DataFixedPtr + inputOffset;
                        var i = 0;
                        while (i < following - 11)
                        {
                            i += 12;
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            *(output + 4) = (char)*(input + 4);
                            *(output + 5) = (char)*(input + 5);
                            *(output + 6) = (char)*(input + 6);
                            *(output + 7) = (char)*(input + 7);
                            *(output + 8) = (char)*(input + 8);
                            *(output + 9) = (char)*(input + 9);
                            *(output + 10) = (char)*(input + 10);
                            *(output + 11) = (char)*(input + 11);
                            output += 12;
                            input += 12;
                        }
                        if (i < following - 5)
                        {
                            i += 6;
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            *(output + 4) = (char)*(input + 4);
                            *(output + 5) = (char)*(input + 5);
                            output += 6;
                            input += 6;
                        }
                        if (i < following - 3)
                        {
                            i += 4;
                            *(output) = (char)*(input);
                            *(output + 1) = (char)*(input + 1);
                            *(output + 2) = (char)*(input + 2);
                            *(output + 3) = (char)*(input + 3);
                            output += 4;
                            input += 4;
                        }
                        while (i < following)
                        {
                            i++;
                            *output = (char)*input;
                            output++;
                            input++;
                        }

                        remaining -= following;
                    }

                    if (remaining == 0)
                    {
                        break;
                    }

                    block = block.Next;
                    inputOffset = block.Start;
                }
            }

            return asciiString;
        }

        public static string GetUtf8String(this MemoryPoolIterator start, MemoryPoolIterator end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return default(string);
            }
            if (end.Block == start.Block)
            {
                return _utf8.GetString(start.Block.Array, start.Index, end.Index - start.Index);
            }

            var decoder = _utf8.GetDecoder();

            var length = start.GetLength(end);
            var charLength = length * 2;
            var chars = new char[charLength];
            var charIndex = 0;

            var block = start.Block;
            var index = start.Index;
            var remaining = length;
            while (true)
            {
                int bytesUsed;
                int charsUsed;
                bool completed;
                var following = block.End - index;
                if (remaining <= following)
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        remaining,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        true,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    return new string(chars, 0, charIndex + charsUsed);
                }
                else if (block.Next == null)
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        following,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        true,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    return new string(chars, 0, charIndex + charsUsed);
                }
                else
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        following,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        false,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    charIndex += charsUsed;
                    remaining -= following;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public static ArraySegment<byte> GetArraySegment(this MemoryPoolIterator start, MemoryPoolIterator end)
        {
            if (start.IsDefault || end.IsDefault)
            {
                return default(ArraySegment<byte>);
            }
            if (end.Block == start.Block)
            {
                return new ArraySegment<byte>(start.Block.Array, start.Index, end.Index - start.Index);
            }

            var length = start.GetLength(end);
            var array = new byte[length];
            start.CopyTo(array, 0, length, out length);
            return new ArraySegment<byte>(array, 0, length);
        }
    }
}
