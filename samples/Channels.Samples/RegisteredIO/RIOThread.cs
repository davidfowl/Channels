// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using Channels;

namespace ManagedRIOHttpServer.RegisteredIO
{
    internal unsafe class RIOThread
    {
        public int id;
        public IntPtr completionPort;
        public IntPtr completionQueue;

        public ConcurrentDictionary<long, RIOTcpConnection> connections;
        public Thread thread;

        public RIOBufferPool bufferPool;
        public MemoryPool memoryPool;
        public ChannelFactory channelFactory;
    }
}
