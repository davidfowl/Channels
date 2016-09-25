using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels
{
    public struct Memory<T>
    {
        private readonly T[] _array;
        private readonly int _offset;
        private readonly int _memoryLength;

        private GCHandle _handle;
        private unsafe void* _memory;

        // If the array passed in is pinned, then unsafe pointer is safe to access even though
        // it's backed by an array, this is an optimization that the creator of Memory<T> can do so
        // that the consumer doesn't need to pin/unpin per operation
        private bool _isUnsafePointerSafe;

        public unsafe Memory(void* pointer, int offset, int length)
        {
            unsafe
            {
                _memory = pointer;
            }

            _array = null;
            _offset = offset;
            _memoryLength = length;
            _isUnsafePointerSafe = true;
            _handle = default(GCHandle);
        }

        public unsafe Memory(T[] array, int offset, int length, bool isPinned = false)
        {
            unsafe
            {
                if (isPinned)
                {
                    // The caller pinned the array so we can safely get the pointer
                    _memory = Unsafe.AsPointer(ref array[0]);
                }
                else
                {
                    _memory = null;
                }
            }

            _array = array;
            _offset = offset;
            _memoryLength = length;
            _isUnsafePointerSafe = isPinned;
            _handle = default(GCHandle);
        }

        public Span<T> Span => this;

        public bool IsEmpty => Length == 0;

        public static implicit operator Span<T>(Memory<T> memory)
        {
            if (memory.Length == 0)
            {
                return Span<T>.Empty;
            }

            if (memory._array != null)
            {
                return memory._array.Slice(memory._offset, memory.Length);
            }
            else
            {
                unsafe
                {
                    return new Span<T>(memory._memory, memory._memoryLength);
                }
            }
        }

        public void Pin()
        {
            if (!_isUnsafePointerSafe)
            {
                _handle = GCHandle.Alloc(_array, GCHandleType.Pinned);
                unsafe
                {
                    _memory = (void*)_handle.AddrOfPinnedObject();
                }

                // This type isn't thread safe
                _isUnsafePointerSafe = true;
            }
        }

        public void Unpin()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();

                unsafe
                {
                    _memory = null;
                }

                _isUnsafePointerSafe = false;
            }
        }

        public unsafe void* UnsafePointer
        {
            get
            {
                if (!_isUnsafePointerSafe)
                {
                    throw new InvalidOperationException("Use Pin() to safely access the native pointer.");
                }
                return (byte*)_memory + (Unsafe.SizeOf<T>() * _offset);
            }
        }

        public int Length => _memoryLength;

        public unsafe Memory<T> Slice(int offset, int length)
        {
            // TODO: Bounds check
            if (_array == null)
            {
                return new Memory<T>(_memory, offset, length);
            }

            return new Memory<T>(_array, _offset + offset, length, _isUnsafePointerSafe);
        }

        public bool TryGetArray(out ArraySegment<T> buffer)
        {
            if (_array == null)
            {
                buffer = default(ArraySegment<T>);
                return false;
            }
            buffer = new ArraySegment<T>(_array, _offset, Length);
            return true;
        }
    }
}
