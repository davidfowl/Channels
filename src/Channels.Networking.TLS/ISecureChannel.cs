using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Networking.TLS
{
    public interface ISecureChannel : IChannel
    {
        Task<ApplicationProtocols.ProtocolIds> HandShakeAsync();
    }
}
