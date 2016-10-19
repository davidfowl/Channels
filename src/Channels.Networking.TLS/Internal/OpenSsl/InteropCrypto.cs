﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal unsafe static class InteropCrypto
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void locking_function(LockState mode, int threadNumber, byte* file, int line);

        private static class WindowsLib
        {
            public const string CryptoDll = "libeay32.dll";
            public const string SslDll = "ssleay32.dll";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void CRYPTO_set_locking_callback(locking_function lockingFunction);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void OPENSSL_add_all_algorithms_noconf();
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void ERR_load_crypto_strings();
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void CRYPTO_free(void* pointer);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static int CRYPTO_num_locks();
            [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void SSL_load_error_strings();
            [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static int SSL_library_init();

            public static void Init()
            {
                CRYPTO_set_locking_callback(LockStore.Callback);
                ERR_load_crypto_strings();
                SSL_load_error_strings();
                OPENSSL_add_all_algorithms_noconf();
                CheckForErrorOrThrow(SSL_library_init());
            }
        }

        private static class UnixLib
        {
            public const string CryptoDll = "libcrypto.so";
            public const string SslDll = "libssl.so";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void CRYPTO_set_locking_callback(locking_function lockingFunction);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void OPENSSL_add_all_algorithms_noconf();
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void ERR_load_crypto_strings();
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void CRYPTO_free(void* pointer);
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static int CRYPTO_num_locks();
            [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void SSL_load_error_strings();
            [DllImport(SslDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static int SSL_library_init();

            public static void Init()
            {
                CRYPTO_set_locking_callback(LockStore.Callback);
                ERR_load_crypto_strings();
                SSL_load_error_strings();
                OPENSSL_add_all_algorithms_noconf();
                CheckForErrorOrThrow(SSL_library_init());
            }
        }

        public static int CRYPTO_num_locks()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsLib.CRYPTO_num_locks();
            }
            else
            {
                return UnixLib.CRYPTO_num_locks();
            }
        }
        public static void CRYPTO_free(void* pointer)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsLib.CRYPTO_free(pointer);
            }
            else
            {
                UnixLib.CRYPTO_free(pointer);
            }
        }

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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsLib.Init();
            }
            else
            {
                UnixLib.Init();
            }
        }
    }
}
