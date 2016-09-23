using System;
using System.Binary;
using System.Runtime.CompilerServices;

namespace Channels
{
    /// <summary>
    /// Common extension methods against readable buffers
    /// </summary>
    public static class DefaultReadableBufferExtensions
    {
        /// <summary>
        /// Reads a structure of type <typeparamref name="T"/> out of a buffer of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadBigEndian<[Primitive]T>(this ReadableBuffer buffer) where T : struct
        {
            var span = buffer.FirstSpan;
            int len = Unsafe.SizeOf<T>();
            var value = span.Length >= len ? span.ReadBigEndian<T>() : ReadMultiBig<T>(buffer, len);
            return value;
        }

        /// <summary>
        /// Reads a structure of type <typeparamref name="T"/> out of a buffer of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadLittleEndian<[Primitive]T>(this ReadableBuffer buffer) where T : struct
        {
            var span = buffer.FirstSpan;
            int len = Unsafe.SizeOf<T>();
            var value = span.Length >= len ? span.ReadLittleEndian<T>() : ReadMultiLittle<T>(buffer, len);
            return value;
        }

        private static unsafe T ReadMultiBig<[Primitive]T>(ReadableBuffer buffer, int len) where T : struct
        {
            byte* local = stackalloc byte[len];
            var localSpan = new Span<byte>(local, len);
            buffer.Slice(0, len).CopyTo(localSpan);
            return localSpan.ReadBigEndian<T>();
        }

        private static unsafe T ReadMultiLittle<[Primitive]T>(ReadableBuffer buffer, int len) where T : struct
        {
            byte* local = stackalloc byte[len];
            var localSpan = new Span<byte>(local, len);
            buffer.Slice(0, len).CopyTo(localSpan);
            return localSpan.ReadLittleEndian<T>();
        }
    }
}
