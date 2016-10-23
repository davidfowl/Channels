using System;
using System.Buffers;
using System.Threading;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : OwnedMemory<byte>, IBuffer
    {
        // REVIEW: We need to expose the underlying ID so we can use up the reference count without using
        // reservations https://github.com/dotnet/corefxlab/issues/914
        private int _referenceCount = 1;

        public Memory<byte> Data => Memory;

        public override void Initialize()
        {
            _referenceCount = 1;

            base.Initialize();
        }

        public IBuffer Preserve()
        {
            Interlocked.Increment(ref _referenceCount);
            return this;
        }

        void IDisposable.Dispose()
        {
            if (Interlocked.Decrement(ref _referenceCount) == 0)
            {
                Dispose();
            }
        }
    }
}
