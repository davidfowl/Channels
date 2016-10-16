using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal unsafe static class InteropBio
    {
        

        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        private extern static BioHandle BIO_new_file(string filename, string mode);
        public static BioHandle BIO_new_file_write(string fileName) => BIO_new_file(fileName, "w");
        public static BioHandle BIO_new_file_read(string fileName) => BIO_new_file(fileName, "r");

        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int BIO_write(BioHandle b, void* buf, int len);
        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int BIO_read(BioHandle b, void* buf, int len);

        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static BioHandle BIO_new(IntPtr type);
        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void BIO_free(IntPtr bio);

        [DllImport(InteropCrypto.CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static IntPtr BIO_s_mem();

        [StructLayout(LayoutKind.Sequential)]
        public struct BioHandle
        {
            public IntPtr Handle;
            public void FreeBio()
            {
                if(Handle != IntPtr.Zero)
                {
                    BIO_free(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        }
    }
}
