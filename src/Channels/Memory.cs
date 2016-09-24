using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Channels
{
    public struct Memory<T>
    {
        private readonly T[] _array;
        private readonly int _offset;
        private readonly unsafe void* _memory;
        private readonly int _memoryLength;

        public unsafe Memory(void* pointer, int offset, int length)
        {
            unsafe
            {
                _memory = pointer;
            }

            _array = null;
            _offset = offset;
            _memoryLength = length;
        }

        public unsafe Memory(T[] array, int offset, int length, void* pointer = null)
        {
            unsafe
            {
                _memory = pointer;
            }

            _array = array;
            _offset = offset;
            _memoryLength = length;
        }

        public Span<T> Span => this;

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

        public unsafe void* UnsafePointer => (byte*)_memory + (Unsafe.SizeOf<T>() * _offset);

        public int Length => _memoryLength;

        public unsafe Memory<T> Slice(int offset, int length)
        {
            // TODO: Bounds check
            if (_array == null)
            {
                return new Memory<T>(_memory, offset, length);
            }

            return new Memory<T>(_array, _offset + offset, length, _memory);
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
