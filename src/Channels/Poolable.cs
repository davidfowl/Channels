using System.Runtime.CompilerServices;
using System.Threading;

namespace Channels
{
    /// <summary>
    /// A base class for any poolable item.
    /// </summary>
    public class Poolable
    {
        int _id;
        int _nextId;

        /// <summary>
        /// A non allocating concurrent pool of any <see cref="Poolable"/>.
        /// </summary>
        /// <typeparam name="T">The poolable type.</typeparam>
        public class ConcurrentPool<T> where T : Poolable
        {
            const int None = -1;

            /// <summary>
            /// Thread-safe collection of blocks which are currently in the pool. A slab will pre-allocate all of the item tracking objects
            /// and add them to this collection. When memory is requested it is taken from here first, and when it is returned it is re-added.
            /// </summary>
            private readonly ObjectIdentityMap<T> _items = new ObjectIdentityMap<T>();

            long _blocksHead = None;

            public void RegisterForPooling(T block)
            {
                block._id = _items.Put(block);
            }

            public void Enqueue(T item)
            {
                if (TryEnqueue(item))
                {
                    return;
                }

                var sw = new SpinWait();
                do
                {
                    sw.SpinOnce();
                } while (TryEnqueue(item));
            }

            bool TryEnqueue(T block)
            {
                var current = Volatile.Read(ref _blocksHead);
                int abaCounter;
                int blockId;
                Decode(current, out abaCounter, out blockId);

                block._nextId = blockId;

                unchecked { abaCounter += 1; }

                return Interlocked.CompareExchange(ref _blocksHead, Encode(abaCounter, block._id), current) == current;
            }

            public bool TryDequeue(out T item)
            {
                // optimistic path
                var current = Volatile.Read(ref _blocksHead);

                int abaCounter;
                int blockId;
                Decode(current, out abaCounter, out blockId);
                if (blockId == None)
                {
                    item = null;
                    return false;
                }

                item = _items.TryRetrieve(blockId);

                unchecked { abaCounter += 1; }

                if (Interlocked.CompareExchange(ref _blocksHead, Encode(abaCounter, item._nextId), current) == current)
                {
                    return true;
                }

                // start spinning
                var sw = new SpinWait();
                sw.SpinOnce();
                do
                {
                    current = Volatile.Read(ref _blocksHead);
                    Decode(current, out abaCounter, out blockId);
                    if (blockId == None)
                    {
                        item = null;
                        return false;
                    }
                    item = _items.TryRetrieve(blockId);
                    unchecked { abaCounter += 1; }
                } while (Interlocked.CompareExchange(ref _blocksHead, Encode(abaCounter, item._nextId), current) != current);

                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe void Decode(long value, out int abaCounter, out int blockId)
            {
                var i = (int*)&value;
                abaCounter = *i;
                blockId = *(i + 1);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe long Encode(int abaCounter, int blockId)
            {
                long result = 0;
                var i = (int*)&result;

                *i = abaCounter;
                *(i + 1) = blockId;

                return result;
            }
        }

    /// <summary>
    /// A map providing a two way mapping between an object and int identifier.
    /// </summary>
    class ObjectIdentityMap<T>
        where T : class
    {
        private volatile Segment _head;
        private volatile Segment _tail;
        private const int SegmentSize = 1024;
        private const int InSegmentMask = SegmentSize - 1;
    
        public ObjectIdentityMap()
        {
            _head = _tail = new Segment(0, this);
        }

        /// <summary>
        /// Puts and item returning its index.
        /// </summary>
        public int Put(T item)
        {
            var sw = new SpinWait();
            while (true)
            {
                var tail = _tail;
                int index;
                if (tail.TryAppend(item, out index))
                {
                    return index;
                }
                sw.SpinOnce();
            }
        }

        /// <summary>
        /// Tries to retrieve a specified item.
        /// </summary>
        public T TryRetrieve(int index)
        {
            return _head.TryRetrieve(index);
        }

        /// <summary>
        /// private class for ConcurrentQueue. 
        /// a queue is a linked list of small arrays, each node is called a segment.
        /// A segment contains an array, a pointer to the next segment, and _low, _high indices recording
        /// the first and last valid elements of the array.
        /// </summary>
        private class Segment
        {
            private volatile T[] _items;
            private volatile Segment _next;
            private readonly int _index;
            private volatile int _previousFreePosition;
            private volatile ObjectIdentityMap<T> _source;

            /// <summary>
            /// Create and initialize a segment with the specified index.
            /// </summary>
            internal Segment(int index, ObjectIdentityMap<T> source)
            {
                _items = new T[SegmentSize];
                _previousFreePosition = -1;
                _index = index;
                _source = source;
            }

            /// <summary>
            /// Create a new segment and append to the current one
            /// Update the m_tail pointer
            /// This method is called when there is no contention
            /// </summary>
            private void Grow()
            {
                //no CAS is needed, since there is no contention (other threads are blocked, busy waiting)
                var newSegment = new Segment(_index + 1, _source);  //m_index is Int64, we don't need to worry about overflow
                _next = newSegment;
                _source._tail = _next;
            }

            /// <summary>
            /// Try to append an element at the end of this segment.
            /// </summary>
            /// <param name="value">the element to append</param>
            /// <param name="index"></param>
            /// <returns>true if the element is appended, false if the current segment is full</returns>
            /// <remarks>if appending the specified element succeeds, and after which the segment is full, 
            /// then grow the segment</remarks>
            internal bool TryAppend(T value, out int index)
            {
                index = 0;

                //quickly check if buffer is already full, if so, return
                if (_previousFreePosition >= SegmentSize - 1)
                {
                    return false;
                }

                int i;

                //We need do Interlocked.Increment and value/state update in a finally block to ensure that they run
                //without interuption. This is to prevent anything from happening between them, and another dequeue
                //thread maybe spinning forever to wait for _items[] to be set;
                try
                { }
                finally
                {
                    i = Interlocked.Increment(ref _previousFreePosition);
                    if (i <= SegmentSize - 1)
                    {
                        index = _index*SegmentSize + i;
                        Volatile.Write(ref _items[i], value);
                    }

                    //if this thread takes up the last slot in the segment, then this thread is responsible
                    //to grow a new segment. Calling Grow must be in the finally block too for reliability reason:
                    //if thread abort during Grow, other threads will be left busy spinning forever.
                    if (i == SegmentSize - 1)
                    {
                        Grow();
                    }
                }

                //if newhigh <= SegmentSize-1, it means the current thread successfully takes up a spot
                return i <= SegmentSize - 1;
            }

            public T TryRetrieve(int index)
            {
                var count = index/SegmentSize;
                var @this = this;

                // move forward
                for (var i = 0; i < count; i++)
                {
                    @this = @this._next;
                    if (@this == null)
                    {
                        return null;
                    }
                }

                return Volatile.Read(ref @this._items[index & InSegmentMask]);
            }
        }
    }

    }
}