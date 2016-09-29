using System;

namespace Channels.Networking.Sockets.Internal
{
    /// <summary>
    /// Stores and retrieves T instances
    /// </summary>
    internal class ObjectPool<T> where T : class
    {
        private readonly IndexPool _available;
        private readonly T[][] _buckets;

        /// <summary>
        /// Create a new ObjectPool instance
        /// </summary>
        public ObjectPool(int capacity = 4096)
        {
            _available = new IndexPool(false, capacity);
            _available.OnTake += OnTake;
            _available.OnPutBack += OnPutBack;

            // split into n buckets of 256 each
            _buckets = new T[capacity >> 8][];
        }

        private object OnPutBack(int index, object context)
        {
            int bucketIndex = index >> 8;
            var bucket = _buckets[bucketIndex] ?? CreateBucket(bucketIndex);
            bucket[index & 0xFF] = (T)context;
            return null;
        }

        private object OnTake(int index, object context)
        {
            var bucket = _buckets[index >> 8];
            if (bucket == null)
            {   // if the bucket doesn't exist,
                // the object doesn't
                return null;
            }

            // fetch the value from the bucket (and clear it)
            var obj = bucket[index & 0xFF];
            bucket[index & 0xFF] = null;
            return obj;
        }

        /// <summary>
        /// Fetch a pooled object (and mark it as taken)
        /// </summary>
        public T TryTake()
        {
            object context;
            int index = _available.TryTake(out context);
            return index < 0 ? null : (T)context;
        }
        public void PutBack(T obj)
        {
            if (obj == null)
            {
                return;
            }
            int index = _available.TryPutBack(obj);
            if (index < 0 && obj is IDisposable)
            {
                // dispose if needed it we haven't got room for it
                ((IDisposable)obj).Dispose();
            }
        }

        private object SyncLock => _buckets;

        private T[] CreateBucket(int bucketIndex)
        {
            lock (SyncLock)
            {
                var bucket = _buckets[bucketIndex];
                if (bucket == null)
                {
                    bucket = _buckets[bucketIndex] = new T[256];
                }
                return bucket;
            }
        }
    }
}
