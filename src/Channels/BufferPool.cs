using System;

namespace Channels
{
    /// <summary>
    /// Convenient base class for implementing an <see cref="IBufferPool"/>.
    /// </summary>
    /// <typeparam name="TBuffer"></typeparam>
    public abstract class BufferPool<TBuffer> : IBufferPool
    {
        /// <summary>
        /// Leases a <typeparamref name="TBuffer"/> from the <see cref="BufferPool{TBuffer}"/>
        /// </summary>
        /// <param name="size">The size of the requested buffer</param>
        /// <returns>A <typeparamref name="TBuffer"/> of at least the requested size</returns>
        public abstract TBuffer Lease(int size);

        /// <summary>
        /// Returns a <see cref="Span{Byte}"/> for the buffer returned in Lease
        /// </summary>
        /// <param name="buffer">A <typeparamref name="TBuffer"/> created by lease</param>
        /// <returns>A <see cref="Span{Byte}"/> that represents the buffer</returns>
        public abstract Span<byte> GetBuffer(TBuffer buffer);

        /// <summary>
        /// Returns the buffer created in Lease to the <see cref="BufferPool{TBuffer}"/>
        /// </summary>
        /// <param name="buffer">A buffer created by Lease</param>
        public abstract void Return(TBuffer buffer);

        /// <summary>
        /// Releases any resources the <see cref="BufferPool{TBuffer}"/> used.
        /// </summary>
        public abstract void Dispose();

        Span<byte> IBufferPool.GetBuffer(object buffer)
        {
            return GetBuffer((TBuffer)buffer);
        }

        PooledBuffer IBufferPool.Lease(int size)
        {
            return new PooledBuffer(this, Lease(size));
        }

        void IBufferPool.Return(object buffer)
        {
            Return((TBuffer)buffer);
        }
    }
}
