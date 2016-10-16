using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal.OpenSsl;

namespace Channels.Networking.TLS
{
    public class OpenSslSecurityContext : IDisposable
    {
        internal const int BlockSize = 1024 * 4 - 64; //Current fixed block size
        
        private bool _initOkay = false;
        private readonly string _hostName;
        private readonly ChannelFactory _channelFactory;
        private readonly bool _isServer;
        private InteropKeys.PK12Certifcate _certifcateInformation;

        public OpenSslSecurityContext(ChannelFactory channelFactory, string hostName, bool isServer, string pathToPfxFile, string password)
        {
            if (isServer && string.IsNullOrEmpty(pathToPfxFile))
            {
                throw new ArgumentException("We need a certificate to load if you want to run in server mode");
            }

            InteropCrypto.Init();
            InteropCrypto.OPENSSL_add_all_algorithms_noconf();
            InteropCrypto.CheckForErrorOrThrow(Interop.SSL_library_init());

            _channelFactory = channelFactory;
            _isServer = isServer;
                        
            InteropBio.BioHandle fileBio = new InteropBio.BioHandle();
            try
            {
                fileBio = InteropBio.BIO_new_file_read(pathToPfxFile);
                //Now we pull out the private key, certificate and Authority if they are all there
                _certifcateInformation = new InteropKeys.PK12Certifcate(fileBio, password);
            }
            finally
            {
                fileBio.FreeBio();
            }
        }

        public bool IsServer => _isServer;
        internal InteropKeys.PK12Certifcate CertificateInformation => _certifcateInformation;

        public ISecureChannel CreateSecureChannel(IChannel channel)
        {
            var chan = new SecureChannel<OpenSslConnectionContext>(channel, _channelFactory, new OpenSslConnectionContext(this));
            return chan;
        }

        public void Dispose()
        {
            _certifcateInformation.Free();
        }
    }
}
