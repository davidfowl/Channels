using System;
using System.Runtime.CompilerServices;

namespace Channels
{
    /// <summary>
    /// Common extension methods against writable buffers
    /// </summary>
    public static class DefaultWritableBufferExtensions
    {
        /// <summary>
        /// Writes the source <see cref="Span{Byte}"/> to the <see cref="WritableBuffer"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="WritableBuffer"/></param>
        /// <param name="source">The <see cref="Span{Byte}"/> to write</param>
        public static void Write(this WritableBuffer buffer, Span<byte> source)
        {
            if (buffer.Memory.IsEmpty)
            {
                buffer.Ensure();
            }

            // Fast path, try copying to the available memory directly
            if (source.TryCopyTo(buffer.Memory))
            {
                buffer.Advance(source.Length);
                return;
            }

            var remaining = source.Length;
            var offset = 0;

            while (remaining > 0)
            {
                var writable = Math.Min(remaining, buffer.Memory.Length);

                buffer.Ensure(writable);

                if (writable == 0)
                {
                    continue;
                }

                source.Slice(offset, writable).TryCopyTo(buffer.Memory);

                remaining -= writable;
                offset += writable;

                buffer.Advance(writable);
            }
        }
        /// <summary>
        /// Reads a structure of type T out of a buffer of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBigEndian<[Primitive]T>(this WritableBuffer buffer, T value) where T : struct
        {
            int len = Unsafe.SizeOf<T>();
            buffer.Ensure(len);
            buffer.Memory.Write<T>(BitConverter.IsLittleEndian ? Reverse(value) : value);
            buffer.Advance(len);
        }

        /// <summary>
        /// Reads a structure of type T out of a buffer of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLittleEndian<[Primitive]T>(this WritableBuffer buffer, T value) where T : struct
        {
            int len = Unsafe.SizeOf<T>();
            buffer.Ensure(len);
            buffer.Memory.Write<T>(BitConverter.IsLittleEndian ? value : Reverse(value));
            buffer.Advance(len);
        }

        /// <summary>
        /// Reverses a primitive value - performs an endianness swap
        /// </summary>
        internal static unsafe T Reverse<[Primitive]T>(T value) where T : struct
        {
            // note: relying on JIT goodness here!
            if (typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte))
            {
                return value;
            }
            else if (typeof(T) == typeof(ushort) || typeof(T) == typeof(short))
            {
                ushort val = 0;
                Unsafe.Write(&val, value);
                val = (ushort)((val >> 8) | (val << 8));
                return Unsafe.Read<T>(&val);
            }
            else if (typeof(T) == typeof(uint) || typeof(T) == typeof(int)
                || typeof(T) == typeof(float))
            {
                uint val = 0;
                Unsafe.Write(&val, value);
                val = (val << 24)
                    | ((val & 0xFF00) << 8)
                    | ((val & 0xFF0000) >> 8)
                    | (val >> 24);
                return Unsafe.Read<T>(&val);
            }
            else if (typeof(T) == typeof(ulong) || typeof(T) == typeof(long)
                || typeof(T) == typeof(double))
            {
                ulong val = 0;
                Unsafe.Write(&val, value);
                val = (val << 56)
                    | ((val & 0xFF00) << 40)
                    | ((val & 0xFF0000) << 24)
                    | ((val & 0xFF000000) << 8)
                    | ((val & 0xFF00000000) >> 8)
                    | ((val & 0xFF0000000000) >> 24)
                    | ((val & 0xFF000000000000) >> 40)
                    | (val >> 56);
                return Unsafe.Read<T>(&val);
            }
            else
            {
                // default implementation
                int len = Unsafe.SizeOf<T>();
                var val = stackalloc byte[len];
                Unsafe.Write(val, value);
                int to = len >> 1, dest = len - 1;
                for (int i = 0; i < to; i++)
                {
                    var tmp = val[i];
                    val[i] = val[dest];
                    val[dest--] = tmp;
                }
                return Unsafe.Read<T>(val);
            }
        }
        /// <summary>
        /// Writes a structure of type T to a span of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBigEndian<[Primitive]T>(this Span<byte> span, T value) where T : struct
            => span.Write(BitConverter.IsLittleEndian ? Reverse<T>(value) : value);

        /// <summary>
        /// Writes a structure of type T to a span of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLittleEndian<[Primitive]T>(this Span<byte> span, T value) where T : struct
            => span.Write(BitConverter.IsLittleEndian ? value : Reverse<T>(value));
    }
}
