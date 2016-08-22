// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Channels;

namespace ManagedRIOHttpServer.RegisteredIO
{
    internal class RIOThreadPool
    {
        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        private RIO _rio;
        private CancellationToken _token;
        private int _maxThreads;

        public const int PreAllocSocketsPerThread = 256;
        private const int MaxOutsandingCompletions = (RIOTcpConnection.MaxPendingReceives + RIOTcpConnection.IOCPOverflowEvents
                                                    + RIOTcpConnection.MaxPendingSends + RIOTcpConnection.IOCPOverflowEvents)
                                                    * PreAllocSocketsPerThread;

        private IntPtr _socket;
        private RIOThread[] _threads;

        public unsafe RIOThreadPool(RIO rio, IntPtr socket, CancellationToken token)
        {
            _socket = socket;
            _rio = rio;
            _token = token;

            _maxThreads = Environment.ProcessorCount;

            _threads = new RIOThread[_maxThreads];
            for (var i = 0; i < _threads.Length; i++)
            {
                IntPtr completionPort = CreateIoCompletionPort(INVALID_HANDLE_VALUE, IntPtr.Zero, 0, 0);

                if (completionPort == IntPtr.Zero)
                {
                    var error = GetLastError();
                    RIOImports.WSACleanup();
                    throw new Exception(string.Format("ERROR: CreateIoCompletionPort returned {0}", error));
                }

                var completionMethod = new RIO_NOTIFICATION_COMPLETION()
                {
                    Type = RIO_NOTIFICATION_COMPLETION_TYPE.IOCP_COMPLETION,
                    Iocp = new RIO_NOTIFICATION_COMPLETION_IOCP()
                    {
                        IocpHandle = completionPort,
                        QueueCorrelation = (ulong)i,
                        Overlapped = (NativeOverlapped*)(-1)// nativeOverlapped
                    }
                };
                IntPtr completionQueue = _rio.CreateCompletionQueue(MaxOutsandingCompletions, completionMethod);

                if (completionQueue == IntPtr.Zero)
                {
                    var error = RIOImports.WSAGetLastError();
                    RIOImports.WSACleanup();
                    throw new Exception(String.Format("ERROR: CreateCompletionQueue returned {0}", error));
                }

                var thread = new RIOThread(i, _token, completionPort, completionQueue, rio);
                _threads[i] = thread;
            }

            // gc
            //GC.Collect(2, GCCollectionMode.Forced, true, true);
            //GC.WaitForPendingFinalizers();
            //GC.Collect(2, GCCollectionMode.Forced, true, true);

            //GC.Collect(2, GCCollectionMode.Forced, true);
            //GC.WaitForPendingFinalizers();
            //GC.Collect(2, GCCollectionMode.Forced, true);

            for (var i = 0; i < _threads.Length; i++)
            {
                // pin buffers
                _threads[i].BufferPool.Initalize();
            }


            for (var i = 0; i < _threads.Length; i++)
            {
                _threads[i].Start();
            }
        }

        internal RIOThread GetThread(long connetionId)
        {
            return _threads[(connetionId % _maxThreads)];
        }

        [DllImport(Kernel_32, SetLastError = true)]
        private unsafe static extern IntPtr CreateIoCompletionPort(long handle, IntPtr hExistingCompletionPort, int puiCompletionKey, uint uiNumberOfConcurrentThreads);

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern long GetLastError();
    }
}
