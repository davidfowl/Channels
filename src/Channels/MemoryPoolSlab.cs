using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Channels
{
    /// <summary>
    /// Slab tracking object used by the byte buffer memory pool. A slab is a large allocation which is divided into smaller blocks. The
    /// individual blocks are then treated as independant array segments.
    /// </summary>
    public class MemoryPoolSlab : IDisposable
    {
        /// <summary>
        /// This handle pins the managed array in memory until the slab is disposed. This prevents it from being
        /// relocated and enables any subsections of the array to be used as native memory pointers to P/Invoked API calls.
        /// </summary>
        private readonly GCHandle _gcHandle;
        private byte[] _data;

        // Native
        private readonly IntPtr _nativeData;
        private readonly int _length;

        private bool _isActive;
        internal Action<MemoryPoolSlab> _deallocationCallback;
        private bool _disposedValue;

        private static int _id;

        public int Id { get; private set; }

        /// <summary>
        /// True as long as the blocks from this slab are to be considered returnable to the pool. In order to shrink the 
        /// memory pool size an entire slab must be removed. That is done by (1) setting IsActive to false and removing the
        /// slab from the pool's _slabs collection, (2) as each block currently in use is Return()ed to the pool it will
        /// be allowed to be garbage collected rather than re-pooled, and (3) when all block tracking objects are garbage
        /// collected and the slab is no longer references the slab will be garbage collected and the memory unpinned will
        /// be unpinned by the slab's Dispose.
        /// </summary>
        public bool IsActive => _isActive;


        public MemoryPoolSlab(IntPtr data, int length)
        {
            _nativeData = data;
            _length = length;
            _isActive = true;

            Id = Interlocked.Increment(ref _id);
        }

        public MemoryPoolSlab(byte[] data)
        {
            _data = data;
            _length = data.Length;
            _gcHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            _isActive = true;

            Id = Interlocked.Increment(ref _id);
        }

        /// <summary>
        /// The span of data this slab represents
        /// </summary>
        public unsafe Span<byte> Data => _nativeData == IntPtr.Zero ? new Span<byte>(_data, 0, _data.Length) : new Span<byte>((byte*)_nativeData, _length);

        public static MemoryPoolSlab CreateNative(int length)
        {
            return new MemoryPoolSlab(Marshal.AllocHGlobal(length), length);
        }

        public static MemoryPoolSlab Create(int length)
        {
            // allocate and pin requested memory length
            var array = new byte[length];

            // allocate and return slab tracking object
            return new MemoryPoolSlab(array);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // N/A: dispose managed state (managed objects).
                }

                _isActive = false;

                _deallocationCallback?.Invoke(this);

                if (_gcHandle.IsAllocated)
                {
                    _gcHandle.Free();
                }
                else
                {
                    Marshal.FreeHGlobal(_nativeData);
                }

                // set large fields to null.
                _data = null;

                _disposedValue = true;
            }
        }

        // override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        ~MemoryPoolSlab()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // uncomment the following line if the finalizer is overridden above.
            GC.SuppressFinalize(this);
        }
    }
}
