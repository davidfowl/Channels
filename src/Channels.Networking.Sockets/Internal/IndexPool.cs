using System;
using System.Threading;

namespace Channels.Networking.Sockets.Internal
{

    /// <summary>
    /// Manages reservations (by index) into an logical pool that is held by the caller. All
    /// spaces are assumed to be available, typically to be populated lazily by the caller upon
    /// discovery. This pool is intended in particular for managing things that are *small*, where
    /// more sophisticated tracking tools would take significantly more space than the item
    /// being pooled.</summary>
    /// <remarks>
    /// Note that there are two modes of use with this pool, depending on whether it is necessary
    /// to preserve order; with TryPutBack(), the index returns the first used slot (and
    /// marks it used) - this is useful for object pools; with PutBack(int), the index restores the same position
    /// - this is useful for tracking sub-buffer availability.</remarks>
    /// <remarks>Note that when event callbacks are used, the callback is invoked inside a page-specific lock; this
    /// removes any race conditions associated with updating the index and any backing store in unison</remarks>
    internal sealed class IndexPool
    {
        private readonly Page _page0;
        private readonly int _maxPageCount;
        private readonly bool _defaultAvailable;

        public event Func<int, object, object> OnPutBack, OnTake;

        public int CountTaken()
        {
            var current = _page0;
            int count = 0;
            while (current != null)
            {
                count += current.CountTaken();
                current = current.Tail;
            }
            return count;
        }

        public int Capacity => _maxPageCount << 8;

        public int CountRemaining() => Capacity - CountTaken();

        public IndexPool(bool defaultAvailable, int count = 4096)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if ((count & 0xFF) != 0)
            {
                throw new ArgumentException($"The count must be a multiple of 256", nameof(count));
            }
            _maxPageCount = count >> 8;
            _defaultAvailable = defaultAvailable;
            _page0 = new Page(defaultAvailable);
        }

        private object SyncLock => this;

        public int TryTake()
        {
            object context = null;
            return TryFind(true, ref context);
        }
        public int TryTake(out object context)
        {
            context = null;
            return TryFind(true, ref context);
        }
        public int TryPutBack(object context = null)
            => TryFind(false, ref context);

        private int TryFind(bool take, ref object context)
        {
            var callback = take ? OnTake : OnPutBack;
            while (true)
            {
                var head = _page0;

                // look for an available entry
                Page current = head, last = null;
                int pageIndex = 0;
                while (current != null)
                {
                    int index;
                    if (current.TryFind(take, pageIndex << 8, out index, ref context, callback))
                    {
                        return index;
                    }
                    last = current;
                    current = current.Tail;
                    pageIndex++;
                }

                // nothing was available; can we add a new page?
                // and would it even be worth *trying* to add a new page?
                if (pageIndex >= _maxPageCount || take != _defaultAvailable)
                {
                    return -1;
                }

                // add a new page; note that we'll double-check inside the lock
                lock (SyncLock)
                {
                    if (last.Tail == null)
                    {
                        last.TrySetTail(new Page(_defaultAvailable));
                    }
                    // now repeat from start (no matter whether it was "us" or "them" that
                    // allocated the new page)                    
                }
            }
        }

        public void PutBack(int index)
        {
            var current = _page0;
            int targetPageIndex = index >> 8, currentPageIndex = 0;
            while (current != null)
            {
                if (targetPageIndex == currentPageIndex++)
                {
                    if (!current.TryPutBack(index & 0xFF))
                    {
                        ThrowPutNotTaken(index);
                    }
                    return;
                }
                current = current.Tail;
            }
            ThrowInvalidIndex(index);
        }
        private void ThrowInvalidIndex(int index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index must be in the range [0,{Capacity}); {index} is invalid");
        }
        private static void ThrowPutNotTaken(int index)
        {
            throw new InvalidOperationException($"Attempted to put back an item that was not taken; index {index}");
        }
        class Page
        {
            private Page _tail;
            private long _available0, _available1, _available2, _available3;

            public bool TryPutBack(int index)
            {
                switch (index >> 6)
                {
                    case 0:
                        return TryPutBack(ref _available0, index);
                    case 1:
                        return TryPutBack(ref _available1, index);
                    case 2:
                        return TryPutBack(ref _available2, index);
                    case 3:
                        return TryPutBack(ref _available3, index);
                }
                ThrowInvalidIndex(index);
                return false;
            }
            private static void ThrowInvalidIndex(int index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            private bool TryPutBack(ref long availability, int index)
            {
                long bit = 1L << (index & 0x3F);
                long snapshot;
                do
                {
                    snapshot = Volatile.Read(ref availability);
                    if ((snapshot & bit) != 0)
                    {
                        return false;
                    }
                } while (Interlocked.CompareExchange(ref availability, snapshot | bit, snapshot) != snapshot);
                return true;
            }
            public Page(bool defaultAvailable)
            {
                _available0 = _available1 = _available2 = _available3 = (defaultAvailable ? ~0 : 0);
            }
            public bool TrySetTail(Page tail)
            {
                return tail != null && Interlocked.CompareExchange(ref _tail, tail, null) == null;
            }
            public Page Tail => _tail;

            public bool TryFind(bool take, int pageIndexMask, out int index, ref object context, Func<int, object, object> callback)
            {
                if (take)
                {
                    return TryTake(ref _available0, pageIndexMask, out index, ref context, callback) || TryTake(ref _available1, pageIndexMask | 64, out index, ref context, callback)
                        || TryTake(ref _available2, pageIndexMask | 128, out index, ref context, callback) || TryTake(ref _available3, pageIndexMask | 192, out index, ref context, callback);
                }
                else
                {
                    return TryPutBack(ref _available0, pageIndexMask, out index, ref context, callback) || TryPutBack(ref _available1, pageIndexMask | 64, out index, ref context, callback)
                        || TryPutBack(ref _available2, pageIndexMask | 128, out index, ref context, callback) || TryPutBack(ref _available3, pageIndexMask | 192, out index, ref context, callback);
                }
            }

            private bool TryTake(ref long availability, int offsetMask, out int index, ref object context, Func<int, object, object> callback)
            {
                long snapshot;
                while ((snapshot = Volatile.Read(ref availability)) != 0)
                {
                    var lsb = snapshot & -snapshot;
                    var newVal = snapshot & ~lsb;
                    if (callback == null)
                    {
                        if (Interlocked.CompareExchange(ref availability, newVal, snapshot) == snapshot)
                        {
                            index = GetLSBIndex64((ulong)lsb) | offsetMask;
                            return true;
                        }
                    }
                    else
                    {
                        lock (SyncLock)
                        {
                            if (Volatile.Read(ref availability) == snapshot)
                            {
                                availability = newVal;
                                index = GetLSBIndex64((ulong)lsb) | offsetMask;
                                context = callback(index, context);
                                return true;
                            }
                        }
                    }
                }
                index = -1;
                return false;
            }

            private object SyncLock => this;

            private bool TryPutBack(ref long availability, int offsetMask, out int index, ref object context, Func<int, object, object> callback)
            {
                long snapshot;
                // NOTE WE'VE INVERTED HERE! we're looking for holes
                while ((snapshot = ~Volatile.Read(ref availability)) != 0)
                {
                    var lsb = snapshot & -snapshot;
                    var newVal = snapshot & ~lsb;
                    if (callback == null)
                    {
                        if (Interlocked.CompareExchange(ref availability, ~newVal, ~snapshot) == ~snapshot)
                        {
                            index = GetLSBIndex64((ulong)lsb) | offsetMask;
                            return true;
                        }
                    }
                    else
                    {
                        lock (SyncLock)
                        {
                            if (Volatile.Read(ref availability) == ~snapshot)
                            {
                                availability = ~newVal;
                                index = GetLSBIndex64((ulong)lsb) | offsetMask;
                                context = callback(index, context);
                                return true;
                            }
                        }
                    }
                }
                index = -1;
                return false;
            }
            public int CountTaken()
            {
                return (NumberOfSetBits64(~(ulong)Volatile.Read(ref _available0)) + NumberOfSetBits64(~(ulong)Volatile.Read(ref _available1))
                    + NumberOfSetBits64(~(ulong)Volatile.Read(ref _available2)) + NumberOfSetBits64(~(ulong)Volatile.Read(ref _available3)));
            }
        }
        // see: https://en.wikipedia.org/wiki/De_Bruijn_sequence
        static readonly int[] MultiplyDeBruijnBitPosition = new int[32]
        {
              0, 1, 28, 2, 29, 14, 24, 3, 30, 22, 20, 15, 25, 17, 4, 8,
              31, 27, 13, 23, 21, 19, 16, 7, 26, 12, 18, 6, 11, 5, 10, 9
        };
        static int GetLSBIndex32(uint v)
            => MultiplyDeBruijnBitPosition[((uint)((v & -v) * 0x077CB531U)) >> 27];

        static int GetLSBIndex64(ulong lsb)
            => ((lsb & 0xFFFFFFFF) == 0) ? (GetLSBIndex32((uint)(lsb >> 32)) | 32) : GetLSBIndex32((uint)lsb);

        static int NumberOfSetBits64(ulong i) // http://stackoverflow.com/questions/2709430/count-number-of-bits-in-a-64-bit-long-big-integer
        {
            i = i - ((i >> 1) & 0x5555555555555555UL);
            i = (i & 0x3333333333333333UL) + ((i >> 2) & 0x3333333333333333UL);
            return (int)(unchecked(((i + (i >> 4)) & 0xF0F0F0F0F0F0F0FUL) * 0x101010101010101UL) >> 56);
        }

    }

}
