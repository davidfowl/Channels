using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal unsafe static class InteropBio
    {
        private static class UnixLib
        {
            public const string CryptoDll = "libcrypto.so";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static BioHandle BIO_new_file(string filename, string mode);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static BioHandle BIO_new(IntPtr type);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void BIO_free(IntPtr bio);
        }

        private static class OsxLib
        {
            public const string CryptoDll = "libcrypto.dylib";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static BioHandle BIO_new_file(string filename, string mode);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static BioHandle BIO_new(IntPtr type);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void BIO_free(IntPtr bio);
        }

        private static class WindowsLib
        {
            public const string CryptoDll = "libeay32.dll";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static BioHandle BIO_new_file(string filename, string mode);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static BioHandle BIO_new(IntPtr type);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void BIO_free(IntPtr bio);
        }

        public static BioHandle BIO_new_file_write(string fileName) => Interop.IsWindows ? WindowsLib.BIO_new_file(fileName, "w") : Interop.IsOsx ? OsxLib.BIO_new_file(fileName, "w") : UnixLib.BIO_new_file(fileName, "w");
        public static BioHandle BIO_new_file_read(string fileName) => Interop.IsWindows ? WindowsLib.BIO_new_file(fileName, "r") : Interop.IsOsx ? OsxLib.BIO_new_file(fileName, "r") : UnixLib.BIO_new_file(fileName, "r");
        public static BioHandle BIO_new(IntPtr type) => Interop.IsWindows ? WindowsLib.BIO_new(type) : Interop.IsOsx ? OsxLib.BIO_new(type) : UnixLib.BIO_new(type);
        public static void BIO_free(IntPtr bio)
        {
            if (Interop.IsWindows)
            {
                WindowsLib.BIO_free(bio);
            }
            else if (Interop.IsOsx)
            {
                OsxLib.BIO_free(bio);
            }
            else
            {
                UnixLib.BIO_free(bio);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BioHandle
        {
            public IntPtr Handle;
            public void FreeBio()
            {
                if (Handle != IntPtr.Zero)
                {
                    BIO_free(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        }
    }
}
