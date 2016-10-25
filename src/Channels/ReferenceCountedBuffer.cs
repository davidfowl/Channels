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

        public IBuffer Preserve()
        {
            AddReference(Id);
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
