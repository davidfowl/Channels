using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public static class MemoryExtensions
    {
        public static unsafe bool TryGetArray<T>(this Memory<T> memory, out ArraySegment<T> buffer)
        {
            return memory.TryGetArray(out buffer, (void*)null);
        }
    }
}
