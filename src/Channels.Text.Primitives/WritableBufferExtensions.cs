using System.Text;

namespace Channels.Text.Primitives
{
    // These APIs suck since you can't pass structs by ref to extension methods and they are mutable structs...
    public static class WritableBufferExtensions
    {
        private static readonly Encoding Utf8Encoding = Encoding.UTF8;
        private static readonly Encoding ASCIIEncoding = Encoding.ASCII;

        public static unsafe void WriteAsciiString(ref WritableBuffer buffer, string value)
        {
            // One byte per char
            buffer.Ensure(value.Length);

            fixed (char* s = value)
            {
                int written = ASCIIEncoding.GetBytes(s, value.Length, (byte*)buffer.Memory.BufferPtr, value.Length);
                buffer.CommitBytes(written);
            }
        }

        public static unsafe void WriteUtf8String(ref WritableBuffer buffer, string value)
        {
            fixed (char* s = value)
            {
                var byteCount = Utf8Encoding.GetByteCount(value);
                buffer.Ensure(byteCount);
                int written = Utf8Encoding.GetBytes(s, value.Length, (byte*)buffer.Memory.BufferPtr, byteCount);
                buffer.CommitBytes(written);
            }
        }

        public static void WriteUInt32(ref WritableBuffer buffer, uint value)
            => WriteUInt64(ref buffer, value);
        public static unsafe void WriteUInt64(ref WritableBuffer buffer, ulong value)
        {
            // optimized versions for 0-1000
            int len;
            byte* addr;
            if (value < 10)
            {
                buffer.Ensure(len = 1);
                addr = (byte*)buffer.Memory.BufferPtr;
                *addr = (byte)('0' + value);
            }
            else if (value < 100)
            {
                buffer.Ensure(len = 2);
                addr = (byte*)buffer.Memory.BufferPtr;
                *addr++ = (byte)('0' + value / 10);
                *addr = (byte)('0' + value % 10);
            }
            else if (value < 1000)
            {
                buffer.Ensure(len = 3);
                addr = (byte*)buffer.Memory.BufferPtr;
                addr[2] = (byte)('0' + value % 10);
                value /= 10;
                *addr++ = (byte)('0' + value / 10);
                *addr = (byte)('0' + value % 10);
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
                addr = (byte*)buffer.Memory.BufferPtr + len;
                do
                {
                    *--addr = (byte)('0' + value % 10);
                    value /= 10;
                } while (value != 0);
            }
            buffer.CommitBytes(len);
        }
    }
}
