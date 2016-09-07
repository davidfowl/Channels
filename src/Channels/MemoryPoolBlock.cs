using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        private List<Operation> operations = new List<Operation>();

        private readonly int _offset;
        private readonly int _length;

        public Span<byte> Data => Slab.Data.Slice(_offset, _length);

        /// <summary>
        /// This object cannot be instantiated outside of the static Create method
        /// </summary>
        protected MemoryPoolBlock(int offset, int length)
        {
            _offset = offset;
            _length = length;

            _referenceCount = 1;
            operations.Add(Operation.Capture());
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

        public string Id => $"Pool:{Pool.GetHashCode()}, Slab:{Slab.Id}, Block:{_offset}), ReferenceCount: {_referenceCount}";

#if DEBUG
        public bool IsLeased { get; set; }
        public string Leaser { get; set; }
#endif

        ~MemoryPoolBlock()
        {
#if DEBUG
            operations.Add(Operation.Capture());

            //foreach (var op in operations)
            //{
            //    Console.WriteLine(op.Name);
            //    Console.WriteLine(op.StackTrace);
            //    Console.WriteLine("------------------------------------------------------------");
            //}

            Debug.Assert(Slab == null || !Slab.IsActive, $"{Environment.NewLine}{Environment.NewLine}*** Block being garbage collected instead of returned to pool: {Leaser} ***{Environment.NewLine}");
#endif
            if (Slab != null && Slab.IsActive)
            {
                Pool.Return(new MemoryPoolBlock(_offset, _length)
                {
                    Pool = Pool,
                    Slab = Slab
                });
            }
        }

        internal static MemoryPoolBlock Create(
            int offset,
            int length,
            MemoryPool pool,
            MemoryPoolSlab slab)
        {
            return new MemoryPoolBlock(offset, length)
            {
                Pool = pool,
                Slab = slab,
#if DEBUG
                Leaser = Environment.StackTrace,
#endif
            };
        }

        /// <summary>
        /// called when the block is returned to the pool. mutable values are re-assigned to their guaranteed initialized state.
        /// </summary>
        public void Reset()
        {
            operations.Add(Operation.Capture());
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
            var data = Data;

            for (int i = 0; i < data.Length; i++)
            {
                builder.Append((char)data[i]);
            }
            return builder.ToString();
        }

        internal void AddReference()
        {
            Interlocked.Increment(ref _referenceCount);
            operations.Add(Operation.Capture());
        }

        internal void RemoveReference()
        {
            operations.Add(Operation.Capture());

            if (Interlocked.Decrement(ref _referenceCount) == 0)
            {
                Pool.Return(this);
            }
        }

        private struct Operation
        {
            public string Name;
            public string StackTrace;

            public static Operation Capture([CallerMemberName] string memberName = "") => new Operation { Name = memberName, StackTrace = Environment.StackTrace };
        }
    }
}