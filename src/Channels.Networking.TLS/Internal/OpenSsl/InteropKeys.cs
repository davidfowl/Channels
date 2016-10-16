using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal static class InteropKeys
    {
        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        private extern static int PKCS12_parse(IntPtr p12, string password, out IntPtr privateKey, out IntPtr certificate, out IntPtr ca);
        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        private extern static void PKCS12_free(IntPtr p12);
        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        private extern static IntPtr d2i_PKCS12_bio(InteropBio.BioHandle inputBio, IntPtr p12);

        public struct PK12Certifcate
        {
            public IntPtr Handle;
            public IntPtr CertificateHandle;
            public IntPtr AuthorityChainHandle;
            public IntPtr PrivateKeyHandle;

            public PK12Certifcate(InteropBio.BioHandle inputBio, string password)
            {
                Handle = d2i_PKCS12_bio(inputBio, IntPtr.Zero);
                PKCS12_parse(Handle, password, out PrivateKeyHandle, out CertificateHandle, out AuthorityChainHandle);
            }

            public void Free()
            {
                if(Handle != IntPtr.Zero)
                {
                    PKCS12_free(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        }
    }
}
