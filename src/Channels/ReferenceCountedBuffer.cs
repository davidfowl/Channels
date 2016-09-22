using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : IBuffer
    {
        private int _referenceCount = 1;

        public abstract Span<byte> Data { get; }

        protected abstract void DisposeBuffer();

        public IBuffer Preserve(int offset, int count)
        {
            // Ignore the offset and count, we're just going to reference count the buffer
            Interlocked.Increment(ref _referenceCount);
            return this;
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _referenceCount) == 0)
            {
                DisposeBuffer();
            }
        }
    }
}
