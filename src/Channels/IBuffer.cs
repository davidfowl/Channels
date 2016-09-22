using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// 
    /// </summary>
    public interface IBuffer : IDisposable
    {
        /// <summary>
        /// Preserves the a portion of the data this buffer represents
        /// </summary>
        /// <param name="offset">The offset to preserve</param>
        /// <param name="length">The length of the buffer to preserve</param>
        IBuffer Preserve(int offset, int length);

        /// <summary>
        /// 
        /// </summary>
        Span<byte> Data { get; }
    }
}
