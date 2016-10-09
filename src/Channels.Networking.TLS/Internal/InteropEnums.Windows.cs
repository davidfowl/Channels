using System;

namespace Channels.Networking.TLS.Internal
{
    [Flags]
    internal enum ContextFlags
    {
        Zero = 0,
        // The communicating parties must authenticate
        // their identities to each other. Without MutualAuth,
        // the client authenticates its identity to the server.
        // With MutualAuth, the server also must authenticate
        // its identity to the client.
        MutualAuth = 0x00000002,
        // The security package detects replayed packets and
        // notifies the caller if a packet has been replayed.
        // The use of this flag implies all of the conditions
        // specified by the Integrity flag.
        ReplayDetect = 0x00000004,
        // The context must be allowed to detect out-of-order
        // delivery of packets later through the message support
        // functions. Use of this flag implies all of the
        // conditions specified by the Integrity flag.
        SequenceDetect = 0x00000008,
        // The context must protect data while in transit.
        // Confidentiality is supported for NTLM with Microsoft
        // Windows NT version 4.0, SP4 and later and with the
        // Kerberos protocol in Microsoft Windows 2000 and later.
        Confidentiality = 0x00000010,
        UseSessionKey = 0x00000020,
        AllocateMemory = 0x00000100,

        // Connection semantics must be used.
        Connection = 0x00000800,

        // Client applications requiring extended error messages specify the
        // ISC_REQ_EXTENDED_ERROR flag when calling the InitializeSecurityContext
        // Server applications requiring extended error messages set
        // the ASC_REQ_EXTENDED_ERROR flag when calling AcceptSecurityContext.
        InitExtendedError = 0x00004000,
        AcceptExtendedError = 0x00008000,
        // A transport application requests stream semantics
        // by setting the ISC_REQ_STREAM and ASC_REQ_STREAM
        // flags in the calls to the InitializeSecurityContext
        // and AcceptSecurityContext functions
        InitStream = 0x00008000,
        AcceptStream = 0x00010000,
        // Buffer integrity can be verified; however, replayed
        // and out-of-sequence messages will not be detected
        InitIntegrity = 0x00010000,       // ISC_REQ_INTEGRITY
        AcceptIntegrity = 0x00020000,       // ASC_REQ_INTEGRITY

        InitManualCredValidation = 0x00080000,   // ISC_REQ_MANUAL_CRED_VALIDATION
        InitUseSuppliedCreds = 0x00000080,   // ISC_REQ_USE_SUPPLIED_CREDS
        InitIdentify = 0x00020000,   // ISC_REQ_IDENTIFY
        AcceptIdentify = 0x00080000,   // ASC_REQ_IDENTIFY

        ProxyBindings = 0x04000000,   // ASC_REQ_PROXY_BINDINGS
        AllowMissingBindings = 0x10000000,   // ASC_REQ_ALLOW_MISSING_BINDINGS

        UnverifiedTargetName = 0x20000000,   // ISC_REQ_UNVERIFIED_TARGET_NAME
    }

    internal enum CredentialUse
    {
        Inbound = 0x1,
        Outbound = 0x2,
        Both = 0x3,
    }

    [Flags]
    internal enum CredentialFlags
    {
        Zero = 0,
        NoSystemMapper = 0x02,
        NoNameCheck = 0x04,
        ValidateManual = 0x08,
        NoDefaultCred = 0x10,
        ValidateAuto = 0x20,
        SendAuxRecord = 0x00200000,
        UseStrongCrypto = 0x00400000,
    }

    internal enum SecurityBufferType
    {
        Empty = 0x00,
        Data = 0x01,
        Token = 0x02,
        Parameters = 0x03,
        Missing = 0x04,
        Extra = 0x05,
        Trailer = 0x06,
        Header = 0x07,
        Padding = 0x09,    // non-data padding
        Stream = 0x0A,
        ChannelBindings = 0x0E,
        Alert = 0x11,
        TargetHost = 0x10,
        ApplicationProtocols = 18,
        ReadOnlyFlag = unchecked((int)0x80000000),
        ReadOnlyWithChecksum = 0x10000000
    }

    internal enum Endianness
    {
        Network = 0x00,
        Native = 0x10,
    }

    internal enum ContextAttribute
    {
        Sizes = 0x00,
        Names = 0x01,
        Lifespan = 0x02,
        DceInfo = 0x03,
        StreamSizes = 0x04,
        Authority = 0x06,
        PackageInfo = 0x0A,
        UniqueBindings = 0x19,
        EndpointBindings = 0x1A,
        ClientSpecifiedSpn = 0x1B,
        RemoteCertificate = 0x53,
        LocalCertificate = 0x54,
        RootStore = 0x55,
        IssuerListInfoEx = 0x59,
        ConnectionInfo = 0x5A,
        ApplicationProtocol = 0x23,
    }
}
