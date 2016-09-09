using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public interface IChannel : IDisposable
    {
        IReadableChannel Input { get; }
        IWritableChannel Output { get; }
    }
}
