using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Channels
{
    /// <summary>
    /// Used to allocate and distribute re-usable blocks of memory.
    /// </summary>
    public class LocalMemoryPool : MemoryPool
    {
        private Func<FinalizableStack> _localStackFactory;
        private ThreadLocal<FinalizableStack> _localStack;
        private FinalizableStack LocalBlocks => _localStack.Value;

        private bool _disposedValue;

        public LocalMemoryPool()
        {
            _localStackFactory = () => new FinalizableStack(this);
            _localStack = new ThreadLocal<FinalizableStack>(_localStackFactory, true);
        }   

        public override IBuffer Lease(int size)
        {
            if (size > _blockStride)
            {
                throw new ArgumentOutOfRangeException(nameof(size), $"Cannot allocate more than {_blockStride} bytes in a single buffer");
            }

            return Lease();
        }

        /// <summary>
        /// Called to take a block from the pool.
        /// </summary>
        /// <returns>The block that is reserved for the called. It must be passed to Return when it is no longer being used.</returns>
#if DEBUG
        private MemoryPoolBlock Lease(
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Debug.Assert(!_disposedValue, "Block being leased from disposed pool!");
#else
        public override MemoryPoolBlock Lease()
        {
#endif
            MemoryPoolBlock block;

            if (!LocalBlocks.TryPop(out block))
            {
                block = base.Lease();
            }

            return block;
        }

        /// <summary>
        /// Called to return a block to the pool. Once Return has been called the memory no longer belongs to the caller, and
        /// Very Bad Things will happen if the memory is read of modified subsequently. If a caller fails to call Return and the
        /// block tracking object is garbage collected, the block tracking object's finalizer will automatically re-create and return
        /// a new tracking object into the pool. This will only happen if there is a bug in the server, however it is necessary to avoid
        /// leaving "dead zones" in the slab due to lost block tracking objects.
        /// </summary>
        /// <param name="block">The block to return. It must have been acquired by calling Lease on the same memory pool instance.</param>
        public override void Return(MemoryPoolBlock block)
        {
#if DEBUG
            Debug.Assert(block.Pool == this, "Returned block was not leased from this pool");
            Debug.Assert(block.IsLeased, $"Block being returned to pool twice: {block.Leaser}{Environment.NewLine}");
            block.IsLeased = false;
#endif

            if (block.Slab != null && block.Slab.IsActive)
            {
                LocalBlocks.Push(block);

                // TODO: Queue timer clean up and call ReturnAllBlocks() on stack
            }
            else
            {
                GC.SuppressFinalize(block);
            }
        }

        private void ReturnMainPool(MemoryPoolBlock block)
        {
            base.Return(block);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                _disposedValue = true;

                var stacks = _localStack.Values;
                    
                // Discard blocks in local pools
                foreach (var stack in stacks)
                {
                    MemoryPoolBlock block;
                    while (stack.TryPop(out block))
                    {
                        GC.SuppressFinalize(block);
                    }
                }

                base.Dispose(true);
            }
        }

        private class FinalizableStack
        {
            private LocalMemoryPool _pool;
            private Stack<MemoryPoolBlock> _stack = new Stack<MemoryPoolBlock>();
            private object _sync = new object();

            public FinalizableStack(LocalMemoryPool pool)
            {
                _pool = pool;
            }

            public bool TryPop(out MemoryPoolBlock item)
            {
                item = null;
                lock (_sync)
                {
                    if (_stack.Count > 0)
                    {
                        item = _stack.Pop();
                    }
                }

                return item != null;
            }

            public void Push(MemoryPoolBlock item)
            {
                lock (_sync)
                {
                    _stack.Push(item);
                }
            }

            public void ReturnAllBlocks()
            {
                lock (_sync)
                {
                    while (_stack.Count > 0)
                    {
                        var block = _stack.Pop();
                        _pool.ReturnMainPool(block);
                    }
                }
            }

            ~FinalizableStack()
            {
                ReturnAllBlocks();
            }
        }
    }
}