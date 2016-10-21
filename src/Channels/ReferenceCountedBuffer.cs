using System;
using System.Buffers;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : OwnedMemory<byte>, IBuffer
    {
        private DisposableReservation _reservation;

        public Memory<byte> Data => Memory;

        protected abstract void DisposeBuffer();

        public IBuffer Preserve(int offset, int count, out int newStart, out int newEnd)
        {
            _reservation = Memory.Reserve();

            // Ignore the offset and count, we're just going to reference count the buffer
            newStart = offset;
            newEnd = offset + count;
            return this;
        }

        public void Dispose()
        {
            _reservation.Dispose();

            if (ReferenceCount == 0)
            {
                DisposeBuffer();

                Initialize();
            }
        }
    }
}
