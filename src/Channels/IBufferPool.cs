using System;

namespace Channels
{
    /// <summary>
    /// An interface that represents a <see cref="IBufferPool"/> that channels will use to allocate memory.
    /// </summary>
    public interface IBufferPool : IDisposable
    {
        /// <summary>
        /// Leases a <see cref="PooledBuffer"/> from the <see cref="IBufferPool"/>
        /// </summary>
        /// <param name="size">The size of the requested buffer</param>
        /// <returns>A <see cref="PooledBuffer"/> which is a wrapper around leased memory</returns>
        PooledBuffer Lease(int size);

        /// <summary>
        /// Returns a <see cref="Span{Byte}"/> for the buffer returned in Lease
        /// </summary>
        /// <param name="buffer">A buffer returned by Lease</param>
        /// <returns>A span that represents the buffer</returns>
        Span<byte> GetBuffer(object buffer);

        /// <summary>
        /// Returns the buffer created in Lease to the <see cref="IBufferPool"/>
        /// </summary>
        /// <param name="buffer">A buffer created by Lease</param>
        void Return(object buffer);
    }
}
