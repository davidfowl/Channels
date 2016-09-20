using System;
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
            var value = span.Length >= len ? span.Read<T>() : ReadMulti<T>(buffer, len);
            return BitConverter.IsLittleEndian ? DefaultWritableBufferExtensions.Reverse<T>(value) : value;
        }

        /// <summary>
        /// Reads a structure of type <typeparamref name="T"/> out of a buffer of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadLittleEndian<[Primitive]T>(this ReadableBuffer buffer) where T : struct
        {
            var span = buffer.FirstSpan;
            int len = Unsafe.SizeOf<T>();
            var value = span.Length >= len ? span.Read<T>() : ReadMulti<T>(buffer, len);
            return BitConverter.IsLittleEndian ? value : DefaultWritableBufferExtensions.Reverse<T>(value);
        }

        private static unsafe T ReadMulti<[Primitive]T>(ReadableBuffer buffer, int len) where T : struct
        {
            byte* local = stackalloc byte[len];
            var localSpan = new Span<byte>(local, len);
            buffer.Slice(0, len).CopyTo(localSpan);
            return localSpan.Read<T>();
        }

        /// <summary>
        /// Reads a structure of type <typeparamref name="T"/> out of a span of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadBigEndian<[Primitive]T>(this Span<byte> span) where T : struct
            => BitConverter.IsLittleEndian ? DefaultWritableBufferExtensions.Reverse<T>(span.Read<T>()) : span.Read<T>();

        /// <summary>
        /// Reads a structure of type <typeparamref name="T"/> out of a span of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadLittleEndian<[Primitive]T>(this Span<byte> span) where T : struct
            => BitConverter.IsLittleEndian ? span.Read<T>() : DefaultWritableBufferExtensions.Reverse<T>(span.Read<T>());
    }
}
