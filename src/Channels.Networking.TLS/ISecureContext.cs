using System;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    public interface ISecureContext : IDisposable
    {
        int TrailerSize { get; set; }
        int HeaderSize { get; set; }
        bool ReadyToSend { get; }
        ApplicationProtocols.ProtocolIds NegotiatedProtocol { get; }
        void ProcessContextMessage(ReadableBuffer readBuffer, ref WritableBuffer writeBuffer);
        void ProcessContextMessage(ref WritableBuffer writeBuffer);
        void Decrypt(ReadableBuffer encryptedData, ref WritableBuffer decryptedData);
        void Encrypt(ReadableBuffer unencrypted, ref WritableBuffer encryptedData);
        bool IsServer { get; }
        CipherInfo CipherInfo { get; }
    }
}