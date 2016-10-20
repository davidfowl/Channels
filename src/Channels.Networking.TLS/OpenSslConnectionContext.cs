using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal.OpenSsl;

namespace Channels.Networking.TLS
{
    public class OpenSslConnectionContext : ISecureContext
    {
        private const Interop.ContextOptions _contextOptions = Interop.ContextOptions.SSL_OP_NO_SSLv2 | Interop.ContextOptions.SSL_OP_NO_SSLv3;

        private readonly OpenSslSecurityContext _securityContext;
        private int _headerSize = 5; //5 is the minimum (1 for frame type, 2 for version, 2 for frame size)
        private int _trailerSize = 16;
        private int _maxDataSize = 16354;
        private bool _readyToSend;
        private IntPtr _ssl;
        private InteropBio.BioHandle _readBio;
        private InteropBio.BioHandle _writeBio;
        private ApplicationProtocols.ProtocolIds _negotiatedProtocol;

        public OpenSslConnectionContext(OpenSslSecurityContext securityContext, IntPtr ssl)
        {
            _ssl = ssl;
            _securityContext = securityContext;
            _writeBio = InteropBio.BIO_new(ChannelBio.Custom());
            _readBio = InteropBio.BIO_new(ChannelBio.Custom());
            
            Interop.SSL_set_bio(_ssl, _readBio, _writeBio);
            if (IsServer)
            {
                Interop.SSL_set_accept_state(_ssl);
            }
            else
            {
                Interop.SSL_set_connect_state(_ssl);
            }
        }

        public bool IsServer => _securityContext.IsServer;
        public int HeaderSize { get { return _headerSize; } set { _headerSize = value; } }
        public int TrailerSize { get { return _trailerSize; } set { _trailerSize = value; } }
        public ApplicationProtocols.ProtocolIds NegotiatedProtocol => _negotiatedProtocol;
        public bool ReadyToSend => _readyToSend;
        public CipherInfo CipherInfo => _ssl != IntPtr.Zero ? Interop.GetCipherInfo(_ssl) : default(CipherInfo);

        public unsafe void Decrypt(ReadableBuffer encryptedData, ref WritableBuffer decryptedData)
        {
            ChannelBio.SetReadBufferPointer(_readBio, ref encryptedData);

            var result = 1;
            while (result > 0)
            {
                void* memPtr;
                decryptedData.Ensure(1024);

                decryptedData.Memory.TryGetPointer(out memPtr);
                result = Interop.SSL_read(_ssl, memPtr, decryptedData.Memory.Length);
                if (result > 0)
                {
                    decryptedData.Advance(result);
                }
            }
        }

        public unsafe void Encrypt(ReadableBuffer unencrypted, ref WritableBuffer encryptedData)
        {
            ChannelBio.SetWriteBufferPointer(_writeBio, ref encryptedData);
            while (unencrypted.Length > 0)
            {
                void* ptr;
                unencrypted.First.TryGetPointer(out ptr);
                var bytesRead = Interop.SSL_write(_ssl, ptr, unencrypted.First.Length);
                unencrypted = unencrypted.Slice(bytesRead);
            }
        }

        public void ProcessContextMessage(ref WritableBuffer writeBuffer)
        {
            ProcessContextMessage(default(ReadableBuffer), ref writeBuffer);
        }

        public unsafe void ProcessContextMessage(ReadableBuffer readBuffer, ref WritableBuffer writeBuffer)
        {
            ChannelBio.SetReadBufferPointer(_readBio, ref readBuffer);
            ChannelBio.SetWriteBufferPointer(_writeBio, ref writeBuffer);

            var result = Interop.SSL_do_handshake(_ssl);
            if (result == 1)
            {
                //handshake is complete, do a final write out of data and mark as done
                //WriteToChannel(ref writeBuffer, _writeBio);
                if (_securityContext.AplnBufferLength > 0)
                {
                    byte* protoPointer;
                    int len;
                    Interop.SSL_get0_alpn_selected(_ssl, out protoPointer, out len);
                    _negotiatedProtocol = ApplicationProtocols.GetNegotiatedProtocol(protoPointer, (byte)len);
                }
                _readyToSend = true;
                return;
            }
            //We didn't get an "okay" message so lets check to see what the actual error was
            var errorCode = Interop.SSL_get_error(_ssl, result);
            if (errorCode == Interop.SslErrorCodes.SSL_NOTHING)
            {
                return;
            }
            if (errorCode == Interop.SslErrorCodes.SSL_WRITING)
            {
                //We have data to write out then return
                //WriteToChannel(ref writeBuffer, _writeBio);
                return;
            }
            if (errorCode == Interop.SslErrorCodes.SSL_READING)
            {
                //We need to read more data so just return to wait for it
                return;
            }
            throw new InvalidOperationException($"There was an error during the handshake, error code was {errorCode}");
        }
        
        public void Dispose()
        {
            _readBio.FreeBio();
            _writeBio.FreeBio();
            if (_ssl != IntPtr.Zero)
            {
                Interop.SSL_free(_ssl);
            }
        }
    }
}
