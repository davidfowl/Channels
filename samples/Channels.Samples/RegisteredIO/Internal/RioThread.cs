// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Channels.Samples.Internal.Winsock;

namespace Channels.Samples.Internal
{
    internal unsafe class RioThread
    {
        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        private readonly RegisteredIO _rio;
        private readonly int _id;
        private IntPtr _completionPort;
        private IntPtr _completionQueue;
        private readonly ConcurrentDictionary<long, RioTcpConnection> _connections = new ConcurrentDictionary<long, RioTcpConnection>();
        private readonly Thread _thread;
        private readonly MemoryPool _memoryPool = new MemoryPool();
        private readonly ChannelFactory _channelFactory;
        private readonly CancellationToken _token;

        private uint _socketsPerThread = 256;

        private uint MaxOutsandingCompletions => 2 * _socketsPerThread;

        public IntPtr ReceiveCompletionQueue => _completionQueue;

        public IntPtr SendCompletionQueue => _completionQueue;

        public IntPtr CompletionPort => _completionPort;

        public int Id => _id;

        public ChannelFactory ChannelFactory => _channelFactory;

        public ConcurrentDictionary<long, RioTcpConnection> Connections => _connections;

        private int _connectionCount;

        public RioThread(int id, CancellationToken token, RegisteredIO rio)
        {
            _id = id;
            _rio = rio;
            _token = token;
            _memoryPool = new MemoryPool();
            _memoryPool.RegisterSlabAllocationCallback(OnSlabAllocated);
            _channelFactory = new ChannelFactory(_memoryPool);
            _connections = new ConcurrentDictionary<long, RioTcpConnection>();
            _thread = new Thread(OnThreadStart);
            _thread.Name = "RIOThread " + id;
            _thread.IsBackground = true;
        }

        public void AddConnection(RioTcpConnection connection)
        {
            Interlocked.Increment(ref _connectionCount);
            _connections.TryAdd(connection.Id, connection);
        }

        public void RemoveConnection(RioTcpConnection connection)
        {
            Interlocked.Decrement(ref _connectionCount);
            RioTcpConnection dummy;
            _connections.TryRemove(connection.Id, out dummy);
        }

        private void Initialize()
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
                    QueueCorrelation = (ulong)_id,
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

            _completionPort = completionPort;
            _completionQueue = completionQueue;
        }

        private object OnSlabAllocated(MemoryPoolSlab slab)
        {
            var bufferId = _rio.RioRegisterBuffer(slab.ArrayPtr, (uint)slab.Array.Length);
            return bufferId;
        }

        private void OnSlabDeallocated(MemoryPoolSlab slab, object state)
        {
            _rio.DeregisterBuffer((IntPtr)state);
        }

        public void Start()
        {
            Initialize();
            _thread.Start(this);
        }

        public bool TryResize()
        {
            if (_connectionCount >= _socketsPerThread)
            {
                _socketsPerThread <<= 1;
                _rio.ResizeCompletionQueue(_completionQueue, MaxOutsandingCompletions);
                return true;
            }

            return false;
        }

        private static void OnThreadStart(object state)
        {
            const int maxResults = 1024;

            var thread = ((RioThread)state);
            var rio = thread._rio;
            var token = thread._token;

            RioRequestResult* results = stackalloc RioRequestResult[maxResults];
            uint bytes, key;
            NativeOverlapped* overlapped;

            var completionPort = thread.CompletionPort;
            var completionQueue = thread.ReceiveCompletionQueue;

            uint count;
            RioRequestResult result;

            while (!token.IsCancellationRequested)
            {
                rio.Notify(completionQueue);
                var success = GetQueuedCompletionStatus(completionPort, out bytes, out key, out overlapped, -1);
                if (success)
                {
                    while ((count = rio.DequeueCompletion(completionQueue, (IntPtr)results, maxResults)) > 0)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            result = results[i];

                            RioTcpConnection connection;
                            if (thread._connections.TryGetValue(result.ConnectionCorrelation, out connection))
                            {
                                connection.Complete(result.RequestCorrelation, result.BytesTransferred);
                            }
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
        private static extern bool GetQueuedCompletionStatus(IntPtr CompletionPort, out uint lpNumberOfBytes, out uint lpCompletionKey, out NativeOverlapped* lpOverlapped, int dwMilliseconds);

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern IntPtr CreateIoCompletionPort(long handle, IntPtr hExistingCompletionPort, int puiCompletionKey, uint uiNumberOfConcurrentThreads);

        [DllImport(Kernel_32, SetLastError = true)]
        private static extern long GetLastError();
    }
}
