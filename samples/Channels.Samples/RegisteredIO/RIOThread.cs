// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Channels;

namespace ManagedRIOHttpServer.RegisteredIO
{
    internal unsafe class RIOThread
    {
        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        private readonly RIO _rio;
        private readonly int _id;
        private readonly IntPtr _completionPort;
        private readonly IntPtr _completionQueue;
        private readonly ConcurrentDictionary<long, RIOTcpConnection> _connections = new ConcurrentDictionary<long, RIOTcpConnection>();
        private readonly Thread _thread;
        private readonly RIOBufferPool _bufferPool;
        private readonly MemoryPool _memoryPool = new MemoryPool();
        private readonly ChannelFactory _channelFactory;
        private readonly CancellationToken _token;

        public IntPtr CompletionQueue => _completionQueue;

        public IntPtr CompletionPort => _completionPort;

        public ChannelFactory ChannelFactory => _channelFactory;

        public RIOBufferPool BufferPool => _bufferPool;

        public ConcurrentDictionary<long, RIOTcpConnection> Connections => _connections;

        public RIOThread(int id, CancellationToken token, IntPtr completionPort, IntPtr completionQueue, RIO rio)
        {
            _id = id;
            _rio = rio;
            _token = token;
            _bufferPool = new RIOBufferPool(rio);
            _channelFactory = new ChannelFactory(_memoryPool);
            _connections = new ConcurrentDictionary<long, RIOTcpConnection>();
            _thread = new Thread(OnThreadStart);
            _thread.Name = "RIOThread " + id;
            _thread.IsBackground = true;
            _completionPort = completionPort;
            _completionQueue = completionQueue;
        }

        public void Start()
        {
            _thread.Start(this);
        }

        private static void OnThreadStart(object state)
        {
            const int maxResults = 1024;

            var thread = ((RIOThread)state);
            var rio = thread._rio;
            var token = thread._token;

            RIO_RESULT* results = stackalloc RIO_RESULT[maxResults];
            uint bytes, key;
            NativeOverlapped* overlapped;

            var completionPort = thread.CompletionPort;
            var completionQueue = thread.CompletionQueue;

            uint count;
            RIO_RESULT result;

            while (!token.IsCancellationRequested)
            {
                rio.Notify(completionQueue);
                var success = GetQueuedCompletionStatus(completionPort, out bytes, out key, out overlapped, -1);
                if (success)
                {
                    var activatedCompletionPort = false;
                    while ((count = rio.DequeueCompletion(completionQueue, (IntPtr)results, maxResults)) > 0)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            result = results[i];
                            if (result.RequestCorrelation >= 0)
                            {
                                // receive
                                RIOTcpConnection connection;
                                if (thread._connections.TryGetValue(result.ConnectionCorrelation, out connection))
                                {
                                    connection.CompleteReceive(result.RequestCorrelation, result.BytesTransferred);
                                }
                            }
                        }

                        if (!activatedCompletionPort)
                        {
                            rio.Notify(completionQueue);
                            activatedCompletionPort = true;
                        }
                    }
                }
                else
                {
                    var error = GetLastError();
                    if (error != 258)
                    {
                        throw new Exception(string.Format("ERROR: GetQueuedCompletionStatusEx returned {0}", error));
                    }
                }
            }
        }

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern unsafe bool GetQueuedCompletionStatus(IntPtr CompletionPort, out uint lpNumberOfBytes, out uint lpCompletionKey, out NativeOverlapped* lpOverlapped, int dwMilliseconds);

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern long GetLastError();
    }
}
