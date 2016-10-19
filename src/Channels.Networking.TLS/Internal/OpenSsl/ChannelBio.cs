using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Channels.Networking.TLS.Internal.OpenSsl
{
    internal static unsafe class ChannelBio
    {
        const int MaxBlockSize = 1024 * 4 - 64;
        static readonly bio_method_st _methodStruct;
        static readonly IntPtr _methodPtr;
        static readonly Create _create;
        static readonly Write _write;
        static readonly Read _read;
        static readonly Control _control;
        static readonly Free _free;

        static ChannelBio()
        {
            _create = CreateBio;
            _write = WriteBio;
            _read = ReadBio;
            _control = ControlBio;
            _free = FreeBio;

            _methodStruct = new bio_method_st()
            {
                create = _create,
                breadDelegate = _read,
                bwriteDelegate = _write,
                ctrlDelegate = _control,
                type = BIO_TYPE_MEM,
                destroy = _free,
                name = (void*)Marshal.StringToCoTaskMemAnsi("ChannelBio")
        };
            var sizeToAlloc = Marshal.SizeOf(_methodStruct);
            _methodPtr = Marshal.AllocHGlobal(sizeToAlloc);
            Marshal.StructureToPtr(_methodStruct, _methodPtr, false);
        }

        public static IntPtr custom() => _methodPtr;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Create(ref bio_st bio);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Write(ref bio_st bio, void* buf, int num);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Read(ref bio_st bio, void* buf, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long Control(ref bio_st bio, BioControl cmd, long num, void* ptr);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Free(ref bio_st data);

        private static class WindowsLib
        {
            public const string CryptoDll = "libeay32.dll";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void BIO_set_flags(ref bio_st bio, BioFlags flags);
        }

        private static class UnixLib
        {
            public const string CryptoDll = "libcrypto.so";
            [DllImport(CryptoDll, CallingConvention = CallingConvention.Cdecl)]
            public extern static void BIO_set_flags(ref bio_st bio, BioFlags flags);
        }
        
        private static void BIO_set_flags(ref bio_st bio, BioFlags flags)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsLib.BIO_set_flags(ref bio, flags);
            }
            else
            {
                UnixLib.BIO_set_flags(ref bio, flags);
            }
        }

        private static int FreeBio(ref bio_st bio)
        {
            bio.init = -1;
            bio.next_bio = null;
            bio.ptr = null;
            return 1;
        }

        private static int CreateBio(ref bio_st bio)
        {
            bio.shutdown = 1;
            bio.init = 1;
            bio.num = -1;
            return 1;
        }

        private static int WriteBio(ref bio_st bio, void* buff, int numberOfBytes)
        {
            var buffer = Unsafe.Read<WritableBuffer>(bio.next_bio);
            int numberOfBytesRemaing = numberOfBytes;
            while (numberOfBytesRemaing > 0)
            {
                int sizeToWrite = Math.Min(MaxBlockSize, numberOfBytesRemaing);
                buffer.Ensure(sizeToWrite);
                buffer.Memory.Span.Set(new Span<byte>(buff, sizeToWrite));
                buffer.Advance(sizeToWrite);
                numberOfBytesRemaing -= sizeToWrite;
            }
            Unsafe.Write(bio.next_bio, buffer);
            return numberOfBytes;
        }

        private static int ReadBio(ref bio_st bio, void* buff, int numberOfBytes)
        {
            var buffer = Unsafe.Read<ReadableBuffer>(bio.ptr);
            if (buffer.Length == 0)
            {
                bio.num = 0;
                if (numberOfBytes > 0)
                {
                    BIO_set_flags(ref bio, BioFlags.BIO_FLAGS_READ | BioFlags.BIO_FLAGS_SHOULD_RETRY);
                }
                return -1;
            }
            if (numberOfBytes == 0)
            {
                return 0;
            }
            numberOfBytes = Math.Min(numberOfBytes, buffer.Length);
            buffer.Slice(0, numberOfBytes).CopyTo(new Span<byte>(buff, numberOfBytes));
            Unsafe.Write(bio.ptr, buffer.Slice(numberOfBytes));
            bio.num = buffer.Length - numberOfBytes;
            return numberOfBytes;
        }

        public static void SetReadBufferPointer(InteropBio.BioHandle bio, ref ReadableBuffer buffer)
        {
            var b = (bio_st*)bio.Handle;
            b[0].num = buffer.Length;
            b[0].ptr = Unsafe.AsPointer(ref buffer);
        }

        public static void SetWriteBufferPointer(InteropBio.BioHandle bio, ref WritableBuffer buffer)
        {
            var b = (bio_st*)bio.Handle;
            b[0].num = buffer.Memory.Length;
            b[0].next_bio = Unsafe.AsPointer(ref buffer);
        }

        private static long ControlBio(ref bio_st bio, BioControl cmd, long num, void* ptr)
        {
            switch (cmd)
            {
                case BioControl.BIO_CTRL_FLUSH:
                case BioControl.BIO_CTRL_POP:
                case BioControl.BIO_CTRL_PUSH:
                    return 1;
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct bio_method_st
        {
            public int type;
            public void* name;
            public Write bwriteDelegate; // int (*bwrite) (BIO*, const char*, int);
            public Read breadDelegate; //int (*bread) (BIO*, char*, int);
            public void* bputsDelegate; //int (*bputs) (BIO*, const char*);
            public void* bgestsDelegate; //int (*bgets) (BIO*, char*, int);
            public Control ctrlDelegate; //long (*ctrl) (BIO*, int, long, void*);
            public Create create; //int (*create) (BIO*);
            public Free destroy; //int (*destroy) (BIO*);
            public void* callback_ctrl; //long (*callback_ctrl) (BIO*, int, bio_info_cb*);
        }
        [StructLayout(LayoutKind.Sequential)]
        struct bio_st
        {
            public void* method;
            /* bio, mode, argp, argi, argl, ret */
            public void* callbackDelegate; //long (*callback) (struct bio_st *, int, const char*, int, long, long);
            public byte* callBackArgsDelegate; //char* cb_arg;               /* first argument for the callback */
            public int init;
            public int shutdown;
            public int flags;                  /* extra storage */
            public int retry_reason;
            public int num;
            public void* ptr;
            public void* next_bio;// struct bio_st *next_bio;    /* used by filter BIOs */
            public void* prev_bio;//struct bio_st *prev_bio;    /* used by filter BIOs */
            public int references;
            public ulong num_read;
            public ulong num_write;
        };

        const int BIO_TYPE_MEM = 1 | 0x0400 | 2;

        [Flags]
        private enum BioFlags
        {
            BIO_FLAGS_READ = 0x01,
            BIO_FLAGS_WRITE = 0x02,
            BIO_FLAGS_IO_SPECIAL = 0x04,
            BIO_FLAGS_RWS = (BIO_FLAGS_READ | BIO_FLAGS_WRITE | BIO_FLAGS_IO_SPECIAL),
            BIO_FLAGS_SHOULD_RETRY = 0x08,
        }

        private enum BioControl
        {
            BIO_CTRL_RESET = 1,/* opt - rewind/zero etc */
            BIO_CTRL_EOF = 2,/* opt - are we at the eof */
            BIO_CTRL_INFO = 3,/* opt - extra tit-bits */
            BIO_CTRL_SET = 4,/* man - set the 'IO' type */
            BIO_CTRL_GET = 5,/* man - get the 'IO' type */
            BIO_CTRL_PUSH = 6,/* opt - internal, used to signify change */
            BIO_CTRL_POP = 7,/* opt - internal, used to signify change */
            BIO_CTRL_GET_CLOSE = 8,/* man - set the 'close' on free */
            BIO_CTRL_SET_CLOSE = 9,/* man - set the 'close' on free */
            BIO_CTRL_PENDING = 10,/* opt - is their more data buffered */
            BIO_CTRL_FLUSH = 11,/* opt - 'flush' buffered output */
            BIO_CTRL_DUP = 12,/* man - extra stuff for 'duped' BIO */
            BIO_CTRL_WPENDING = 13,/* opt - number of bytes still to write */
        }
    }
}
