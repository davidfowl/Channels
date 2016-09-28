using System;
using System.Diagnostics;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that is completely owned by this object.
    /// </summary>
    public class OwnedBuffer : IBuffer
    {
        private byte[] _buffer;

        public OwnedBuffer(byte[] buffer)
        {
            _buffer = buffer;
        }

        public Memory<byte> Data => new Memory<byte>(_buffer);

        public void Dispose()
        {
            // No need, the GC can handle it.
        }

        // We're owned, we're always "preserved"
        public IBuffer Preserve(int offset, int length, out int newStart, out int newEnd)
        {
            newStart = 0;
            newEnd = Data.Length;
            return this;
        }
    }
}