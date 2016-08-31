using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public interface IWritableChannel
    {
        Task Completion { get; }

        WritableBuffer Alloc(int minimumSize = 0);

        void CompleteWriting(Exception error = null);
    }
}
