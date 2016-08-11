using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public interface IWritableChannel
    {
        WritableBuffer BeginWrite(int minimumSize = 0);
        Task EndWriteAsync(WritableBuffer end);

        void CompleteWriting(Exception error = null);
    }
}
