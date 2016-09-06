using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Samples.Http
{
    public interface ITcpConnectionFeature
    {
        IReadableChannel Input { get; }
        IWritableChannel Output { get; }
    }
}
