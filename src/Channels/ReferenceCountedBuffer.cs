using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : IBuffer
    {
        private int _referenceCount = 1;

        public abstract Memory<byte> Data { get; }

        protected abstract void DisposeBuffer();

        public IBuffer Preserve(int offset, int count, out int newStart, out int newEnd)
        {
            Interlocked.Increment(ref _referenceCount);

            // Ignore the offset and count, we're just going to reference count the buffer
            newStart = offset;
            newEnd = offset + count;
            return this;
        }

        public void Dispose()
        {
            Debug.Assert(_referenceCount >= 0, "Too many calls to dispose!");

            var count = Interlocked.Decrement(ref _referenceCount);

            if (count == 0)
            {
                DisposeBuffer();

                // Reset the reference count after disposing this buffer
                Interlocked.Exchange(ref _referenceCount, 1);
            }
        }
    }
}
