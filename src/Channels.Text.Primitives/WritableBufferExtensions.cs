using System;
using System.Text;

namespace Channels.Text.Primitives
{
    // These APIs suck since you can't pass structs by ref to extension methods and they are mutable structs...
    public static class WritableBufferExtensions
    {
        private static readonly Encoding Utf8Encoding = Encoding.UTF8;
        private static readonly Encoding ASCIIEncoding = Encoding.ASCII;

        public static void WriteAsciiString(this WritableBuffer buffer, string value)
            => WriteString(buffer, value, ASCIIEncoding);

        public static void WriteUtf8String(this WritableBuffer buffer, string value)
            => WriteString(buffer, value, Utf8Encoding);

        // review: make public?
        private static unsafe void WriteString(this WritableBuffer buffer, string value, Encoding encoding)
        {
            int bytesPerChar = encoding.GetMaxByteCount(1);
            fixed (char* s = value)
            {
                int remainingChars = value.Length, charOffset = 0;
                while (remainingChars != 0)
                {
                    buffer.Ensure(bytesPerChar);

                    var memory = buffer.Memory;
                    var charsThisBatch = Math.Min(remainingChars, memory.Length / bytesPerChar);

                    int bytesWritten = encoding.GetBytes(s + charOffset, charsThisBatch,
                        (byte*)buffer.RawMemory.UnsafePointer, memory.Length);

                    charOffset += charsThisBatch;
                    remainingChars -= charsThisBatch;
                    buffer.Advance(bytesWritten);
                }
            }
        }

        // REVIEW: See if we can use IFormatter here
        public static void WriteUInt32(this WritableBuffer buffer, uint value) => WriteUInt64(buffer, value);

        public static void WriteUInt64(this WritableBuffer buffer, ulong value)
        {
            // optimized versions for 0-1000
            int len;
            if (value < 10)
            {
                buffer.Ensure(len = 1);
                var span = buffer.Memory;
                span[0] = (byte)('0' + value);
            }
            else if (value < 100)
            {
                buffer.Ensure(len = 2);
                var span = buffer.Memory;
                span[0] = (byte)('0' + value / 10);
                span[1] = (byte)('0' + value % 10);
            }
            else if (value < 1000)
            {
                buffer.Ensure(len = 3);
                var span = buffer.Memory;
                span[2] = (byte)('0' + value % 10);
                value /= 10;
                span[0] = (byte)('0' + value / 10);
                span[1] = (byte)('0' + value % 10);
            }
            else
            {

                // more generic version for all other numbers; first find the number of digits;
                // lost of ways to do this, but: http://stackoverflow.com/a/6655759/23354
                ulong remaining = value;
                len = 1;
                if (remaining >= 10000000000000000) { remaining /= 10000000000000000; len += 16; }
                if (remaining >= 100000000) { remaining /= 100000000; len += 8; }
                if (remaining >= 10000) { remaining /= 10000; len += 4; }
                if (remaining >= 100) { remaining /= 100; len += 2; }
                if (remaining >= 10) { remaining /= 10; len += 1; }
                buffer.Ensure(len);

                // now we'll walk *backwards* from the last character, adding the digit each time
                // and dividing by 10
                int index = len - 1;
                var span = buffer.Memory;

                do
                {
                    span[index--] = (byte)('0' + value % 10);
                    value /= 10;
                } while (value != 0);
            }
            buffer.Advance(len);
        }
    }
}
