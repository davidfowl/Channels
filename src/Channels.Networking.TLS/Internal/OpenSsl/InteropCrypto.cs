using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal unsafe static class InteropCrypto
    {
        public const string CryptoDll = "libeay32.dll";

        [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void OPENSSL_add_all_algorithms_noconf();
        [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void ERR_load_crypto_strings();

        [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static int CRYPTO_num_locks();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void locking_function(LockState mode, int threadNumber, byte* file, int line);
        [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
        public extern static void CRYPTO_set_locking_callback(locking_function lockingFunction);

        [Flags]
        internal enum LockState
        {
            CRYPTO_UNLOCK = 0x02,
            CRYPTO_READ = 0x04,
            CRYPTO_LOCK = 0x01,
            CRYPTO_WRITE = 0x08,
        }
        
        public static void CheckForErrorOrThrow(int returnCode)
        {
            if (returnCode != 1)
            {
                throw new System.Security.SecurityException("Ssl Exception");
            }
        }

        public static void Init()
        {
            CRYPTO_set_locking_callback(LockStore.Callback);
            ERR_load_crypto_strings();
            Interop.SSL_load_error_strings();
        }
    }
}
