using System;
using System.Buffers;
using System.Threading;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : OwnedMemory<byte>, IBuffer
    {
        public Memory<byte> Data => Memory;

        public override void Initialize()
        {
            base.Initialize();

            AddReference(Id);
        }

        public IBuffer Preserve(int offset, int count, out int newStart, out int newEnd)
        {
            AddReference(Id);

            // Ignore the offset and count, we're just going to reference count the buffer
            newStart = offset;
            newEnd = offset + count;
            return this;
        }

        protected override void OnReferenceCountChanged(int newReferenceCount)
        {
            if (newReferenceCount == 0)
            {
                Dispose();
            }
        }

        void IDisposable.Dispose()
        {
            ReleaseReference(Id);
        }
    }
}
