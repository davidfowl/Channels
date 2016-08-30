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
                buffer.UpdateWritten(written);
            }
        }

        public static unsafe void WriteUtf8String(ref WritableBuffer buffer, string value)
        {
            fixed (char* s = value)
            {
                var byteCount = Utf8Encoding.GetByteCount(value);
                buffer.Ensure(byteCount);
                int written = Utf8Encoding.GetBytes(s, value.Length, (byte*)buffer.Memory.BufferPtr, byteCount);
                buffer.UpdateWritten(written);
            }
        }
    }
}
