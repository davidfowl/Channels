using System;

namespace Channels
{
    /// <summary>
    /// An interface that represents a <see cref="IBufferPool"/> that channels will use to allocate memory.
    /// </summary>
    public interface IBufferPool : IDisposable
    {
        // REVIEW: Do we need a size?

        /// <summary>
        /// Leases a <see cref="PooledBuffer"/> from the <see cref="IBufferPool"/>
        /// </summary>
        /// <returns>A <see cref="PooledBuffer"/> which is a wrapper around leased memory</returns>
        PooledBuffer Lease();

        // REVIEW: The interface looks a bit asymmetric but that's in purpose so that
        // LeasedBuffer doesn't need to expose any of the behind the scenes tracking objects
        // of the underlying pool

        /// <summary>
        /// Returns the tracking object created in Lease to the <see cref="IBufferPool"/>
        /// </summary>
        /// <param name="trackingObject">A tracking object created by Lease</param>
        void Return(object trackingObject);
    }
}
