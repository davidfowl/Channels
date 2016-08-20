using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public interface IWritableChannel
    {
        WritableBuffer Alloc(int minimumSize = 0);
        Task WriteAsync(WritableBuffer buffer);

        void CompleteWriting(Exception error = null);
    }
}
