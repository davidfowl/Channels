using System;

namespace Channels.Networking.Sockets.Internal
{
    internal class MicroBufferPool
    {
        private byte[] _rawBuffer;
        private readonly int _bytesPerItem, _count;
        private readonly IndexPool _pool;
        public MicroBufferPool(int bytesPerItem, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerItem));
            }
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerItem));
            }
            _bytesPerItem = bytesPerItem;
            _count = count;
            _pool = new IndexPool(true, count);
        }

        private byte[] Buffer => _rawBuffer ?? CreateBuffer();

        private byte[] CreateBuffer()
        {
            lock (_pool)
            {
                return _rawBuffer ?? (_rawBuffer = new byte[_bytesPerItem * _count]);
            }
        }

        public int BytesPerItem => _bytesPerItem;

        public bool TryTake(out ArraySegment<byte> buffer)
        {
            int index = _pool.TryTake();
            if (index < 0)
            {
                buffer = default(ArraySegment<byte>);
                return false;
            }
            buffer = new ArraySegment<byte>(Buffer, index * _bytesPerItem, _bytesPerItem);
            return true;
        }

        public bool TryPutBack(ArraySegment<byte> buffer)
        {
            if (buffer.Array != Buffer || buffer.Count != _bytesPerItem
                || (buffer.Offset % _bytesPerItem) != 0)
            {
                // not our buffer, or not a slice we would have handed out
                return false;
            }
            int index = buffer.Offset / _bytesPerItem;
            _pool.PutBack(index);
            return true;
        }

        public int CountRemaining() => _pool.CountRemaining();
    }
}
