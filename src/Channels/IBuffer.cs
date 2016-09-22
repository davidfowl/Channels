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
        /// Preserves the data this buffer represents
        /// </summary>
        IBuffer Preserve();

        /// <summary>
        /// 
        /// </summary>
        Span<byte> Data { get; }
    }
}
