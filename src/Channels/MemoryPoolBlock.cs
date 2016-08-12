using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Channels
{
    /// <summary>
    /// Block tracking object used by the byte buffer memory pool. A slab is a large allocation which is divided into smaller blocks. The
    /// individual blocks are then treated as independent array segments.
    /// </summary>
    public class MemoryPoolBlock
    {
        /// <summary>
        /// Native address of the first byte of this block's Data memory. It is null for one-time-use memory, or copied from 
        /// the Slab's ArrayPtr for a slab-block segment. The byte it points to corresponds to Data.Array[0], and in practice you will always
        /// use the DataArrayPtr + Start or DataArrayPtr + End, which point to the start of "active" bytes, or point to just after the "active" bytes.
        /// </summary>
        public readonly IntPtr DataArrayPtr;

        internal unsafe readonly byte* DataFixedPtr;

        /// <summary>
        /// The array segment describing the range of memory this block is tracking. The caller which has leased this block may only read and
        /// modify the memory in this range.
        /// </summary>
        public ArraySegment<byte> Data;

        /// <summary>
        /// This object cannot be instantiated outside of the static Create method
        /// </summary>
        unsafe protected MemoryPoolBlock(IntPtr dataArrayPtr)
        {
            DataArrayPtr = dataArrayPtr;
            DataFixedPtr = (byte*)dataArrayPtr.ToPointer();
            _referenceCount = 1;
        }

        /// <summary>
        /// Back-reference to the memory pool which this block was allocated from. It may only be returned to this pool.
        /// </summary>
        public MemoryPool Pool { get; private set; }

        /// <summary>
        /// Back-reference to the slab from which this block was taken, or null if it is one-time-use memory.
        /// </summary>
        public MemoryPoolSlab Slab { get; private set; }

        /// <summary>
        /// Convenience accessor
        /// </summary>
        public byte[] Array => Data.Array;

        private int _referenceCount;

#if DEBUG
        public bool IsLeased { get; set; }
        public string Leaser { get; set; }
#endif

        ~MemoryPoolBlock()
        {
#if DEBUG
            Debug.Assert(Slab == null || !Slab.IsActive, $"{Environment.NewLine}{Environment.NewLine}*** Block being garbage collected instead of returned to pool: {Leaser} ***{Environment.NewLine}");
#endif
            if (Slab != null && Slab.IsActive)
            {
                Pool.Return(new MemoryPoolBlock(DataArrayPtr)
                {
                    Data = Data,
                    Pool = Pool,
                    Slab = Slab,
                });
            }
        }

        internal static MemoryPoolBlock Create(
            ArraySegment<byte> data,
            IntPtr dataPtr,
            MemoryPool pool,
            MemoryPoolSlab slab)
        {
            return new MemoryPoolBlock(dataPtr)
            {
                Data = data,
                Pool = pool,
                Slab = slab,
            };
        }

        /// <summary>
        /// called when the block is returned to the pool. mutable values are re-assigned to their guaranteed initialized state.
        /// </summary>
        public void Reset()
        {
            _referenceCount = 1;
        }

        /// <summary>
        /// ToString overridden for debugger convenience. This displays the "active" byte information in this block as ASCII characters.
        /// ToString overridden for debugger convenience. This displays the byte information in this block as ASCII characters.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < Data.Count; i++)
            {
                builder.Append(Array[i + Data.Offset].ToString("X2"));
                builder.Append(" ");
            }
            return builder.ToString();
        }

        internal void AddReference()
        {
            Interlocked.Increment(ref _referenceCount);
        }

        internal void RemoveReference()
        {
            if (Interlocked.Decrement(ref _referenceCount) == 0)
            {
                Pool.Return(this);
            }
        }
    }
}