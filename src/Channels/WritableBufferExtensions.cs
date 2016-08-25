using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Channels
{
    public static class WritableBufferExtensions
    {
        public static unsafe void WriteAsciiString(ref WritableBuffer buffer, string value)
        {
            // One byte per char
            buffer.Ensure(value.Length);

            fixed (char* s = value)
            {
                int written = Encoding.ASCII.GetBytes(s, value.Length, (byte*)buffer.Memory.BufferPtr, value.Length);
                buffer.UpdateWritten(written);
            }
        }

        public static unsafe void WriteUtf8String(ref WritableBuffer buffer, string value)
        {
            var encoding = Encoding.UTF8;

            fixed (char* s = value)
            {
                var byteCount = encoding.GetByteCount(value);
                buffer.Ensure(byteCount);
                int written = encoding.GetBytes(s, value.Length, (byte*)buffer.Memory.BufferPtr, byteCount);
                buffer.UpdateWritten(written);
            }
        }
    }
}
