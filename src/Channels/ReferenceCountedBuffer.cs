using System;
using System.Buffers;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : OwnedMemory<byte>, IBuffer
    {
        public ReferenceCountedBuffer(byte[] buffer, int offset, int length, IntPtr pointer = default(IntPtr)) 
            : base(buffer, offset, length, pointer)
        {
            AddReference();
        }

        public IBuffer Preserve()
        {
            AddReference();
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
            Release();
        }
    }
}
