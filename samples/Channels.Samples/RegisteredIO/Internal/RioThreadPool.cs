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

        private IntPtr _socket;
        private RioThread[] _rioThreads;

        public unsafe RioThreadPool(RegisteredIO rio, IntPtr socket, CancellationToken token)
        {
            _socket = socket;
            _rio = rio;
            _token = token;

            _maxThreads = 1;//Environment.ProcessorCount;

            _rioThreads = new RioThread[_maxThreads];
            for (var i = 0; i < _rioThreads.Length; i++)
            {
                var thread = new RioThread(i, _token, rio);
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
    }
}
