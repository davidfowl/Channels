using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal.OpenSsl;

namespace Channels.Networking.TLS
{
    public class OpenSslConnectionContext : ISecureContext
    {
        private readonly OpenSslSecurityContext _securityContext;
        private int _headerSize = 5; //5 is the minimum (1 for frame type, 2 for version, 2 for frame size)
        private int _trailerSize = 16;
        private int _maxDataSize = 16354;
        private bool _readyToSend;
        private IntPtr _sslContext;
        private IntPtr _ssl;
        private InteropBio.BioHandle _readBio;
        private InteropBio.BioHandle _writeBio;

        public OpenSslConnectionContext(OpenSslSecurityContext securityContext)
        {
            _securityContext = securityContext;
            if (_securityContext.IsServer)
            {
                _sslContext = Interop.NewServerContext();
            }
            else
            {
                _sslContext = Interop.NewClientContext();
            }
            Interop.SSL_CTX_set_verify(_sslContext, Interop.VerifyMode.SSL_VERIFY_NONE, IntPtr.Zero);
            var certInfo = _securityContext.CertificateInformation;
            if (certInfo.Handle != IntPtr.Zero)
            {
                if (certInfo.CertificateHandle != IntPtr.Zero)
                {
                    InteropCrypto.CheckForErrorOrThrow(Interop.SSL_CTX_use_certificate(_sslContext, certInfo.CertificateHandle));
                }
                if (certInfo.PrivateKeyHandle != IntPtr.Zero)
                {
                    InteropCrypto.CheckForErrorOrThrow(Interop.SSL_CTX_use_PrivateKey(_sslContext, certInfo.PrivateKeyHandle));
                }
            }
            _ssl = Interop.SSL_new(_sslContext);
            _writeBio = InteropBio.BIO_new(InteropBio.BIO_s_mem());
            _readBio = InteropBio.BIO_new(InteropBio.BIO_s_mem());
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
        //Not implemented yet!!
        public ApplicationProtocols.ProtocolIds NegotiatedProtocol => 0;
        public bool ReadyToSend => _readyToSend;

        public unsafe void Decrypt(ReadableBuffer encryptedData, ref WritableBuffer decryptedData)
        {
            while (encryptedData.Length > 0)
            {
                void* ptr;
                encryptedData.First.TryGetPointer(out ptr);
                var bytesRead = InteropBio.BIO_write(_readBio, ptr, encryptedData.First.Length);
                encryptedData = encryptedData.Slice(bytesRead);
            }

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
            while(unencrypted.Length > 0)
            {
                void* ptr;
                unencrypted.First.TryGetPointer(out ptr);
                var bytesRead = Interop.SSL_write(_ssl, ptr, unencrypted.First.Length);
                unencrypted = unencrypted.Slice(bytesRead);
            }
            //Read off the encrypted data to the writeable buffer
            WriteToChannel(ref encryptedData, _writeBio);
        }

        public void ProcessContextMessage(ref WritableBuffer writeBuffer)
        {
            ProcessContextMessage(default(ReadableBuffer), ref writeBuffer);
        }

        public unsafe void ProcessContextMessage(ReadableBuffer readBuffer, ref WritableBuffer writeBuffer)
        {
            while(readBuffer.Length > 0)
            {
                void* ptr;
                readBuffer.First.TryGetPointer(out ptr);
                var bytesRead = InteropBio.BIO_write(_readBio, ptr, readBuffer.First.Length);
                readBuffer = readBuffer.Slice(bytesRead);
            }

            var result = Interop.SSL_do_handshake(_ssl);
            if(result == 1)
            {
                //handshake is complete, do a final write out of data and mark as done
                WriteToChannel(ref writeBuffer, _writeBio);
                _readyToSend = true;
                return;
            }
            //We didn't get an "okay" message so lets check to see what the actual error was
            var errorCode = Interop.SSL_get_error(_ssl, result);
            if(errorCode == Interop.SslErrorCodes.SSL_NOTHING)
            {
                return;
            }
            if(errorCode == Interop.SslErrorCodes.SSL_WRITING)
            {
                //We have data to write out then return
                WriteToChannel(ref writeBuffer, _writeBio);
                return;
            }
            if(errorCode == Interop.SslErrorCodes.SSL_READING)
            {
                //We need to read more data so just return to wait for it
                return;
            }
            throw new InvalidOperationException($"There was an error during the handshake, error code was {errorCode}");
        }

        private unsafe void WriteToChannel(ref WritableBuffer buffer, InteropBio.BioHandle bio)
        {
            void* memPtr;
            buffer.Ensure(1024);
            buffer.Memory.TryGetPointer(out memPtr);
            var result = InteropBio.BIO_read(bio, memPtr, buffer.Memory.Length);

            while (result > 0)
            {
                buffer.Advance(result);
                buffer.Ensure(1024);

                buffer.Memory.TryGetPointer(out memPtr);
                result = InteropBio.BIO_read(bio, memPtr, buffer.Memory.Length);
            }
        }

        public void Dispose()
        {
            _readBio.FreeBio();
            _writeBio.FreeBio();
            if(_sslContext != IntPtr.Zero)
            {
                //todo kill context
            }
        }
    }
}
