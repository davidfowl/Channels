// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using IllyriadGames.River.Internal.Winsock;

namespace IllyriadGames.River.Internal
{
    internal class ThreadPool
    {
        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        private Winsock.RegisteredIO _rio;
        private CancellationToken _token;
        private int _maxThreads;

        public const int PreAllocSocketsPerThread = 256;
        private const int MaxOutsandingCompletions = (TcpConnection.MaxPendingReceives + TcpConnection.IOCPOverflowEvents
                                                    + TcpConnection.MaxPendingSends + TcpConnection.IOCPOverflowEvents)
                                                    * PreAllocSocketsPerThread;

        private IntPtr _socket;
        private Thread[] _threads;

        public unsafe ThreadPool(Winsock.RegisteredIO rio, IntPtr socket, CancellationToken token)
        {
            _socket = socket;
            _rio = rio;
            _token = token;

            _maxThreads = Environment.ProcessorCount;

            _threads = new Thread[_maxThreads];
            for (var i = 0; i < _threads.Length; i++)
            {
                IntPtr completionPort = CreateIoCompletionPort(INVALID_HANDLE_VALUE, IntPtr.Zero, 0, 0);

                if (completionPort == IntPtr.Zero)
                {
                    var error = GetLastError();
                    RioImports.WSACleanup();
                    throw new Exception(string.Format("ERROR: CreateIoCompletionPort returned {0}", error));
                }

                var completionMethod = new NotificationCompletion()
                {
                    Type = NotificationCompletionType.IocpCompletion,
                    Iocp = new NotificationCompletionIocp()
                    {
                        IocpHandle = completionPort,
                        QueueCorrelation = (ulong)i,
                        Overlapped = (NativeOverlapped*)(-1)// nativeOverlapped
                    }
                };
                IntPtr completionQueue = _rio.RioCreateCompletionQueue(MaxOutsandingCompletions, completionMethod);

                if (completionQueue == IntPtr.Zero)
                {
                    var error = RioImports.WSAGetLastError();
                    RioImports.WSACleanup();
                    throw new Exception(String.Format("ERROR: RioCreateCompletionQueue returned {0}", error));
                }

                var thread = new Thread(i, _token, completionPort, completionQueue, rio);
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

        internal Thread GetThread(long connetionId)
        {
            return _threads[(connetionId % _maxThreads)];
        }

        [DllImport(Kernel_32, SetLastError = true)]
        private unsafe static extern IntPtr CreateIoCompletionPort(long handle, IntPtr hExistingCompletionPort, int puiCompletionKey, uint uiNumberOfConcurrentThreads);

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern long GetLastError();
    }
}
