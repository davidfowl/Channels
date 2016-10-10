using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Channels.Networking.TLS.Internal;

namespace Channels.Networking.TLS
{
    public class SecurityContext: IDisposable
    {
        internal const ContextFlags RequiredFlags = ContextFlags.ReplayDetect | ContextFlags.SequenceDetect | ContextFlags.Confidentiality | ContextFlags.AllocateMemory;
        internal const ContextFlags ServerRequiredFlags = RequiredFlags | ContextFlags.AcceptStream;
        internal const int BlockSize = 1024 * 4 - 64; //Current fixed block size
        private const string SecurityPackage = "Microsoft Unified Security Protocol Provider";
        public const int MaxStackAllocSize = 32 * 1024;

        private bool _initOkay = false;
        private int _maxTokenSize;
        private bool _isServer;
        private X509Certificate _serverCertificate;
        private SslProtocols _supportedProtocols = SslProtocols.Tls;
        private SSPIHandle _credsHandle;
        private string _hostName;
        private byte[] _alpnSupportedProtocols;
        private GCHandle _alpnHandle;
        private SecurityBuffer _alpnBuffer;
        private ChannelFactory _channelFactory;
        
        /// <summary>
        /// Loads up SSPI and sets up the credentials handle in memory ready to authenticate TLS connections
        /// </summary>
        /// <param name="factory">The channel factory that will be used to allocate input and output channels for the secure channels</param>
        /// <param name="hostName">The name of the host that will be sent to the other parties, for a server this should be the name on the certificate. For clients this can be left blank or the name on a client cert</param>
        /// <param name="isServer">Used to denote if you are going to be negotiating incoming or outgoing Tls connections</param>
        /// <param name="serverCert">This is the in memory representation of the certificate used for the PKI exchange and authentication</param>
        public SecurityContext(ChannelFactory factory, string hostName, bool isServer, X509Certificate serverCert)
            : this(factory, hostName, isServer, serverCert, 0)
        {
        }

        /// <summary>
        /// Loads up SSPI and sets up the credentials handle in memory ready to authenticate TLS connections
        /// </summary>
        /// <param name="factory">The channel factory that will be used to allocate input and output channels for the secure channels</param>
        /// <param name="hostName">The name of the host that will be sent to the other parties, for a server this should be the name on the certificate. For clients this can be left blank or the name on a client cert</param>
        /// <param name="isServer">Used to denote if you are going to be negotiating incoming or outgoing Tls connections</param>
        /// <param name="serverCert">This is the in memory representation of the certificate used for the PKI exchange and authentication</param>
        /// <param name="alpnSupportedProtocols">This is the protocols that are supported and that will be negotiated with on the otherside, if a protocol can't be negotiated then the handshake will fail</param>
        public SecurityContext(ChannelFactory factory, string hostName, bool isServer, X509Certificate serverCert, ApplicationProtocols.ProtocolIds alpnSupportedProtocols)
        {
            if (hostName == null)
            {
                throw new ArgumentNullException(nameof(hostName));
            }
            _hostName = hostName;
            _channelFactory = factory;
            _serverCertificate = serverCert;
            _isServer = isServer;
            CreateAuthentication(alpnSupportedProtocols);
        }

        internal SSPIHandle CredentialsHandle => _credsHandle;
        internal bool AplnRequired => _alpnSupportedProtocols != null;
        internal SecurityBuffer AplnBuffer => _alpnBuffer;
        internal string HostName => _hostName;
        public bool IsServer => _isServer;

        private unsafe void CreateAuthentication(ApplicationProtocols.ProtocolIds alpnSupportedProtocols)
        {
            int numberOfPackages;
            SecPkgInfo* secPointer = null;
            if (alpnSupportedProtocols > 0)
            {
                //We need to get a buffer for the ALPN negotiation and pin it for sending to the lower API
                _alpnSupportedProtocols = ApplicationProtocols.GetBufferForProtocolId(alpnSupportedProtocols);
                _alpnHandle = GCHandle.Alloc(_alpnSupportedProtocols, GCHandleType.Pinned);
                _alpnBuffer = new SecurityBuffer((void*)_alpnHandle.AddrOfPinnedObject(), _alpnSupportedProtocols.Length, SecurityBufferType.ApplicationProtocols);
            }
            try
            {
                //Load the available security packages and look for the Unified pack from MS that supplies TLS support
                if (InteropSspi.EnumerateSecurityPackagesW(out numberOfPackages, out secPointer) != 0)
                {
                    throw new InvalidOperationException("Unable to enumerate security packages");
                }
                var size = sizeof(SecPkgInfo);
                for (int i = 0; i < numberOfPackages; i++)
                {
                    var package = secPointer[i];
                    var name = Marshal.PtrToStringUni(package.Name);
                    if (name == SecurityPackage)
                    {
                        _maxTokenSize = package.cbMaxToken;

                        //The correct security package is available
                        _initOkay = true;
                        GetCredentials();
                        return;
                    }
                }
                throw new InvalidOperationException($"Unable to find the security package named {SecurityPackage}");
            }
            finally
            {
                if (secPointer != null)
                {
                    InteropSspi.FreeContextBuffer((IntPtr)secPointer);
                }
            }
        }

        private unsafe void GetCredentials()
        {
            CredentialUse direction;
            CredentialFlags flags;
            if (_isServer)
            {
                direction = CredentialUse.Inbound;
                flags = CredentialFlags.UseStrongCrypto | CredentialFlags.SendAuxRecord;
            }
            else
            {
                direction = CredentialUse.Outbound;
                flags = CredentialFlags.ValidateManual | CredentialFlags.NoDefaultCred | CredentialFlags.SendAuxRecord | CredentialFlags.UseStrongCrypto;
            }

            var creds = new SecureCredential()
            {
                rootStore = IntPtr.Zero,
                phMappers = IntPtr.Zero,
                palgSupportedAlgs = IntPtr.Zero,
                cMappers = 0,
                cSupportedAlgs = 0,
                dwSessionLifespan = 0,
                reserved = 0,
                dwMinimumCipherStrength = 0, //this is required to force encryption
                dwMaximumCipherStrength = 0,
                version = SecureCredential.CurrentVersion,
                dwFlags = flags,
                certContextArray = IntPtr.Zero,
                cCreds = 0
            };

            IntPtr certPointer;
            if (_isServer)
            {
                creds.grbitEnabledProtocols = InteropSspi.ServerProtocolMask;
                certPointer = _serverCertificate.Handle;
                //pointer to the pointer
                IntPtr certPointerPointer = new IntPtr(&certPointer);
                creds.certContextArray = certPointerPointer;
                creds.cCreds = 1;
            }
            else
            {
                creds.grbitEnabledProtocols = InteropSspi.ClientProtocolMask;
            }

            long timestamp = 0;
            SecurityStatus code = (SecurityStatus)InteropSspi.AcquireCredentialsHandleW(null, SecurityPackage, (int)direction
                , null, ref creds, null, null, ref _credsHandle, out timestamp);

            if (code != SecurityStatus.OK)
            {
                throw new InvalidOperationException($"Could not acquire the credentials with return code {code}");
            }
        }

        public SecureChannel CreateSecureChannel(IChannel channel)
        {
            var chan = new SecureChannel(channel, _channelFactory);
            chan.StartReading(new SecureConnectionContext(this));
            return chan;
        }

        public void Dispose()
        {
            if (_credsHandle.IsValid)
            {
                InteropSspi.FreeCredentialsHandle(ref _credsHandle);
                _credsHandle = new SSPIHandle();
            }
            if (_alpnHandle.IsAllocated)
            {
                _alpnHandle.Free();
            }
            GC.SuppressFinalize(this);
        }

        ~SecurityContext()
        {
            Dispose();
        }
    }
}