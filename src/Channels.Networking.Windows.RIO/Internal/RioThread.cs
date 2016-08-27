// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Channels.Networking.Windows.RIO.Internal.Winsock;

namespace Channels.Networking.Windows.RIO.Internal
{
    internal unsafe class RioThread
    {
        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        private readonly RegisteredIO _rio;
        private readonly int _id;
        private readonly IntPtr _completionPort;
        private readonly IntPtr _completionQueue;
        private readonly ConcurrentDictionary<long, RioTcpConnection> _connections = new ConcurrentDictionary<long, RioTcpConnection>();
        private readonly ConcurrentDictionary<IntPtr, IntPtr> _bufferIdMappings = new ConcurrentDictionary<IntPtr, IntPtr>();
        private readonly Thread _thread;
        private readonly MemoryPool _memoryPool = new MemoryPool();
        private readonly ChannelFactory _channelFactory;
        private readonly CancellationToken _token;

        public IntPtr ReceiveCompletionQueue => _completionQueue;

        public IntPtr SendCompletionQueue => _completionQueue;

        public IntPtr CompletionPort => _completionPort;

        public ChannelFactory ChannelFactory => _channelFactory;

        public ConcurrentDictionary<long, RioTcpConnection> Connections => _connections;

        public RioThread(int id, CancellationToken token, IntPtr completionPort, IntPtr completionQueue, RegisteredIO rio)
        {
            _id = id;
            _rio = rio;
            _token = token;
            _memoryPool = new MemoryPool();
            _memoryPool.RegisterSlabAllocationCallback(OnSlabAllocated);
            _memoryPool.RegisterSlabDeallocationCallback(OnSlabDeallocated);
            _channelFactory = new ChannelFactory(_memoryPool);
            _connections = new ConcurrentDictionary<long, RioTcpConnection>();
            _thread = new Thread(OnThreadStart);
            _thread.Name = "RIOThread " + id;
            _thread.IsBackground = true;
            _completionPort = completionPort;
            _completionQueue = completionQueue;
        }

        public IntPtr GetBufferId(IntPtr address)
        {
            IntPtr bufferId;
            if (_bufferIdMappings.TryGetValue(address, out bufferId))
            {
                return bufferId;
            }
            return IntPtr.Zero;
        }

        private void OnSlabAllocated(MemoryPoolSlab slab)
        {
            var bufferId = _rio.RioRegisterBuffer(slab.ArrayPtr, (uint)slab.Array.Length);

            _bufferIdMappings[slab.ArrayPtr] = bufferId;
        }

        private void OnSlabDeallocated(MemoryPoolSlab slab)
        {
            IntPtr bufferId;
            if (_bufferIdMappings.TryRemove(slab.ArrayPtr, out bufferId))
            {
                _rio.DeregisterBuffer(bufferId);
            }
            else
            {
                Debug.Assert(false, "Unknown buffer id!");
            }
        }

        public void Start()
        {
            _thread.Start(this);
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
                            var result = results[i];

                            RioTcpConnection connection;
                            if (thread._connections.TryGetValue(result.ConnectionCorrelation, out connection))
                            {
                                connection.Complete(result.Status, result.RequestCorrelation, result.BytesTransferred);
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
        private static extern long GetLastError();
    }
}
