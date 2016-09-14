using System;
using System.Threading;

namespace Channels
{
    /// <summary>
    /// Leased buffer from the <see cref="IBufferPool"/>
    /// </summary>
    public struct PooledBuffer
    {
        private IBufferPool _pool;
        private object _trackingObject;
        private int _refCount;

        public PooledBuffer(IBufferPool pool, object trackingObject)
        {
            _pool = pool;
            _trackingObject = trackingObject;
            _refCount = 1;
        }

        public Span<byte> Data => _trackingObject == null ? Span<byte>.Empty : _pool.GetBuffer(_trackingObject);

        // Keep these internal for now since nobody needs to use these but the channels system
        internal void AddReference()
        {
            Interlocked.Increment(ref _refCount);
        }

        internal void RemoveReference()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                _pool.Return(_trackingObject);
            }
        }
    }
}
