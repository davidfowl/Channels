using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a channel from which data can be written.
    /// </summary>
    public interface IWritableChannel
    {
        /// <summary>
        /// Gets a task that completes when the consumer is completed reading.
        /// </summary>
        /// <remarks>When this task is triggered, the producer should stop producing data.</remarks>
        Task Completion { get; }

        /// <summary>
        /// Allocates memory from the channel to write into.
        /// </summary>
        /// <param name="minimumSize">The minimum size buffer to allocate</param>
        /// <returns>A <see cref="WritableBuffer"/> that can be written to.</returns>
        WritableBuffer Alloc(int minimumSize = 0);

        /// <summary>
        /// Marks the channel as being complete, meaning no more items will be written to it.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the channel to complete.</param>
        void CompleteWriting(Exception exception = null);
    }
}
