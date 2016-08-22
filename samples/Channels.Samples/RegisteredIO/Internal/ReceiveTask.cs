// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace IllyriadGames.River.Internal
{
    public sealed class ReceiveTask
    {
        private uint _requestCorrelation;
        internal PooledSegment _segment;
        private TcpConnection _connection;

        public ReceiveTask(TcpConnection connection, PooledSegment segment)
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

        private bool disposedValue = false; // To detect redundant calls

        internal void Dispose()
        {
            if (!disposedValue)
            {
                disposedValue = true;
                _segment.Dispose();
            }
        }

    }
}
