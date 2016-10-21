using System;
using System.Buffers;
using System.Collections.Generic;

namespace Channels
{
    public abstract class ReferenceCountedBuffer : OwnedMemory<byte>, IBuffer
    {
        private Stack<DisposableReservation> _reservations = new Stack<DisposableReservation>();

        public Memory<byte> Data => Memory;

        protected abstract void DisposeBuffer();

        public IBuffer Preserve(int offset, int count, out int newStart, out int newEnd)
        {
            lock (_reservations)
            {
                _reservations.Push(Memory.Reserve());
            }

            // Ignore the offset and count, we're just going to reference count the buffer
            newStart = offset;
            newEnd = offset + count;
            return this;
        }

        public void Dispose()
        {
            lock (_reservations)
            {
                if (_reservations.Count > 0)
                {
                    _reservations.Pop().Dispose();
                }
            }

            if (ReferenceCount == 0)
            {
                Dispose();

                DisposeBuffer();
            }
        }
    }
}
