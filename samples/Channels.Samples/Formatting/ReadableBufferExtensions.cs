using System;
using System.Diagnostics;
using System.Text;
using System.Text.Utf8;

namespace Channels.Samples
{
    public static class ReadableBufferExtensions
    {
        public static uint ReadUInt32(this ReadableBuffer buffer)
        {
            Debug.Assert(buffer.IsSingleSpan, "Multi span buffers not supported yet");

            uint value;
            var utf8Buffer = new Utf8String(buffer.FirstSpan.Array, buffer.FirstSpan.Offset, buffer.FirstSpan.Length);
            if (!InvariantParser.TryParse(utf8Buffer, out value))
            {
                throw new InvalidOperationException();
            }
            return value;
        }
    }
}
