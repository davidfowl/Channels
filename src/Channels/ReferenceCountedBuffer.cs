using System;
using System.Buffers;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : OwnedMemory<byte>
    {
        public ReferenceCountedBuffer(byte[] buffer, int offset, int length, IntPtr pointer = default(IntPtr)) 
            : base(buffer, offset, length, pointer)
        {
            AddReference();
        }

        protected override void OnReferenceCountChanged(int newReferenceCount)
        {
            if (newReferenceCount == 0)
            {
                Dispose();
            }
        }
    }
}
