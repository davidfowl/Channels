// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public sealed class RIOReceiveTask
    {
        private uint _requestCorrelation;
        internal RIOPooledSegment _segment;
        private RIOTcpConnection _connection;

        public RIOReceiveTask(RIOTcpConnection connection, RIOPooledSegment segment)
        {
            _segment = segment;
            _connection = connection;
        }

        internal void Complete(uint bytesTransferred, uint requestCorrelation)
        {
            _requestCorrelation = requestCorrelation;

            if (bytesTransferred > 0)
            {
                var buffer = _connection.Input.Alloc();

                buffer.Write(_segment.Buffer, _segment.Offset, (int)bytesTransferred);

                _connection.Input.WriteAsync(buffer);

                _connection.PostReceive(_requestCorrelation);
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        internal void Dispose()
        {
            if (!disposedValue)
            {
                disposedValue = true;
                _segment.Dispose();
            }
        }

        #endregion

    }
}
