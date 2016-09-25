using System;
using System.Diagnostics;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : IBuffer
    {
        private object _lockObj = new object();
        private int _referenceCount = 1;

        public abstract Span<byte> Data { get; }

        protected abstract void DisposeBuffer();

        public IBuffer Preserve(int offset, int count)
        {
            lock (_lockObj)
            {
                _referenceCount++;
            }

            // Ignore the offset and count, we're just going to reference count the buffer
            return this;
        }

        public void Dispose()
        {
            lock (_lockObj)
            {
                Debug.Assert(_referenceCount >= 0, "Too many calls to dispose!");

                _referenceCount--;

                if (_referenceCount == 0)
                {
                    DisposeBuffer();

                    // Reset the reference count after disposing this buffer
                    _referenceCount = 1;
                }
            }
        }
    }
}
