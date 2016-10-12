using System;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    public interface ISecureContext : IDisposable
    {
        int TrailerSize { get; set; }
        int HeaderSize { get; set; }
        SSPIHandle ContextHandle { get; }
        bool ReadyToSend { get; }
        ApplicationProtocols.ProtocolIds NegotiatedProtocol { get; }
        void ProcessContextMessage(ReadableBuffer readBuffer, WritableBuffer writeBuffer);
        void ProcessContextMessage(WritableBuffer writeBuffer);
        bool IsServer { get;}
    }
}