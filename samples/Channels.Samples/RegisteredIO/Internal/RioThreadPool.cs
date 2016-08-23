// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Channels.Samples.Internal.Winsock;

namespace Channels.Samples.Internal
{
    internal class RioThreadPool
    {
        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        private RegisteredIO _rio;
        private CancellationToken _token;
        private int _maxThreads;

        public const int PreAllocSocketsPerThread = 256;
        private const int MaxOutsandingCompletions = 2 * PreAllocSocketsPerThread;

        private IntPtr _socket;
        private RioThread[] _rioThreads;

        public unsafe RioThreadPool(RegisteredIO rio, IntPtr socket, CancellationToken token)
        {
            _socket = socket;
            _rio = rio;
            _token = token;

            _maxThreads = Environment.ProcessorCount;

            _rioThreads = new RioThread[_maxThreads];
            for (var i = 0; i < _rioThreads.Length; i++)
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

                var thread = new RioThread(i, _token, completionPort, completionQueue, rio);
                _rioThreads[i] = thread;
            }

            for (var i = 0; i < _rioThreads.Length; i++)
            {
                var thread = _rioThreads[i];
                thread.Start();
            }
        }

        internal RioThread GetThread(long connetionId)
        {
            return _rioThreads[(connetionId % _maxThreads)];
        }

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern IntPtr CreateIoCompletionPort(long handle, IntPtr hExistingCompletionPort, int puiCompletionKey, uint uiNumberOfConcurrentThreads);

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern long GetLastError();
    }
}
