using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal unsafe static class Interop
    {
        public const string SslDll = "ssleay32.dll";

        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void SSL_load_error_strings();
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_library_init();

        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_CTX_use_PrivateKey(IntPtr ctx, IntPtr pkey);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_CTX_use_certificate(IntPtr ctx, IntPtr cert);

        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_write(IntPtr ssl, void* buf, int len);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_read(IntPtr ssl, void* buf, int len);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void SSL_set_connect_state(IntPtr ssl);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void SSL_set_accept_state(IntPtr sll);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int SSL_do_handshake(IntPtr ssl);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static IntPtr SSL_CTX_new(IntPtr sslMethod);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static IntPtr SSL_new(IntPtr sslContext);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void SSL_set_bio(IntPtr ssl, InteropBio.BioHandle readBio, InteropBio.BioHandle writeBio);
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static SslErrorCodes SSL_get_error(IntPtr ssl, int errorIndetity);

        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static IntPtr SSLv23_client_method();
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static IntPtr SSLv23_server_method();
        private static readonly IntPtr ServerMethod = Interop.SSLv23_server_method();
        private static readonly IntPtr ClientMethod = Interop.SSLv23_client_method();
        public static IntPtr NewServerContext() => SSL_CTX_new(ServerMethod);
        public static IntPtr NewClientContext() => SSL_CTX_new(ClientMethod);

        [Flags]
        public enum VerifyMode : int
        {
            SSL_VERIFY_NONE = 0x00,
            SSL_VERIFY_PEER = 0x01,
            SSL_VERIFY_FAIL_IF_NO_PEER_CERT = 0x02,
            SSL_VERIFY_CLIENT_ONCE = 0x04,
        }
        [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void SSL_CTX_set_verify(IntPtr context, VerifyMode mode, IntPtr callback);

        public enum SslErrorCodes
        {
            SSL_NOTHING = 1,
            SSL_WRITING = 2,
            SSL_READING = 3,
        }
    }
}
