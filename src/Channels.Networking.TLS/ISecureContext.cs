using System;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    public interface ISecureContext : IDisposable
    {
        int TrailerSize { get; set; }
        int HeaderSize { get; set; }
        bool ReadyToSend { get; }
        ApplicationProtocols.ProtocolIds NegotiatedProtocol { get; }
        Task ProcessContextMessageAsync(ReadableBuffer readBuffer, IWritableChannel writeChannel);
        Task ProcessContextMessageAsync(IWritableChannel writeChannel);
        Task DecryptAsync(ReadableBuffer encryptedData, IWritableChannel decryptedData);
        Task EncryptAsync(ReadableBuffer unencrypted, IWritableChannel encryptedData);
        bool IsServer { get; }
        CipherInfo CipherInfo { get; }
    }
}