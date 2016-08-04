using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public interface IWritableChannel
    {
        MemoryPoolIterator BeginWrite(int minimumSize = 0);
        Task EndWriteAsync(MemoryPoolIterator end);

        void CompleteWriting(Exception error = null);
    }
}
