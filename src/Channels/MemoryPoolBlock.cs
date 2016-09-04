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
        /// The array segment describing the range of memory this block is tracking. The caller which has leased this block may only read and
        /// modify the memory in this range.
        /// </summary>
        public Span<byte> Data;

        /// <summary>
        /// This object cannot be instantiated outside of the static Create method
        /// </summary>
        unsafe protected MemoryPoolBlock(Span<byte> data)
        {
            Data = data;
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
                Pool.Return(new MemoryPoolBlock(Data)
                {
                    Pool = Pool,
                    Slab = Slab,
                });
            }
        }

        internal static MemoryPoolBlock Create(
            Span<byte> data,
            MemoryPool pool,
            MemoryPoolSlab slab)
        {
            return new MemoryPoolBlock(data)
            {
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
            for (int i = 0; i < Data.Length; i++)
            {
                builder.Append(Data[i].ToString("X2"));
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