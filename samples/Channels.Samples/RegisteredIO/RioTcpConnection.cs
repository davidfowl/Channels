// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Channels.Samples.Internal;
using Channels.Samples.Internal.Winsock;

namespace Channels.Samples
{
    public sealed class RioTcpConnection : IDisposable
    {
        public const int MaxPendingReceives = 16;
        public const int MaxPendingSends = MaxPendingReceives * 2;
        public const int IOCPOverflowEvents = 8;
        const int ReceiveMask = MaxPendingReceives - 1;
        const int SendMask = MaxPendingSends - 1;

        long _connectionId;
        IntPtr _socket;
        IntPtr _requestQueue;
        RegisteredIO _rio;
        RioThread _rioThread;

        long _sendCount = 0;
        long _receiveRequestCount = 0;

        ReceiveTask[] _receiveTasks;
        PooledSegment[] _sendSegments;

        public MemoryPoolChannel Input { get; }
        public MemoryPoolChannel Output { get; }
        public ChannelFactory ChannelFactory => _rioThread.ChannelFactory;

        internal RioTcpConnection(IntPtr socket, long connectionId, IntPtr requestQueue, RioThread rioThread, RegisteredIO rio)
        {
            _socket = socket;
            _connectionId = connectionId;
            _rio = rio;
            _rioThread = rioThread;

            Input = ChannelFactory.CreateChannel();
            Output = ChannelFactory.CreateChannel();

            _requestQueue = requestQueue;

            _receiveTasks = new ReceiveTask[MaxPendingReceives];

            for (var i = 0; i < _receiveTasks.Length; i++)
            {
                _receiveTasks[i] = new ReceiveTask(this, rioThread.BufferPool.GetBuffer());
            }

            _sendSegments = new PooledSegment[MaxPendingSends];
            for (var i = 0; i < _sendSegments.Length; i++)
            {
                _sendSegments[i] = rioThread.BufferPool.GetBuffer();
            }

            rioThread.Connections.TryAdd(connectionId, this);

            for (var i = 0; i < _receiveTasks.Length; i++)
            {
                PostReceive(i);
            }

            ProcessSends();
        }

        private async void ProcessSends()
        {
            while (true)
            {
                await Output;

                var begin = Output.BeginRead();
                var end = begin;

                if (begin.IsEnd && Output.Completion.IsCompleted)
                {
                    break;
                }

                BufferSpan span;
                while (begin.TryGetBuffer(out span))
                {
                    QueueSend(span.Buffer, isEnd: false);
                }

                FlushSends();

                Output.EndRead(begin);
            }
        }

        const RioSendFlags MessagePart = RioSendFlags.Defer | RioSendFlags.DontNotify;
        const RioSendFlags MessageEnd = RioSendFlags.None;

        int _currentOffset = 0;

        public unsafe void FlushSends()
        {
            var segment = _sendSegments[_sendCount & SendMask];
            if (_currentOffset > 0)
            {
                segment.RioBuffer.Length = (uint)_currentOffset;
                if (!_rio.Send(_requestQueue, &segment.RioBuffer, 1, MessageEnd, -_sendCount - 1))
                {
                    ReportError("Flush");
                }
                _currentOffset = 0;
                _sendCount++;
            }
        }

        public unsafe void QueueSend(ArraySegment<byte> buffer, bool isEnd)
        {
            var segment = _sendSegments[_sendCount & SendMask];
            var count = buffer.Count;
            var offset = buffer.Offset;

            do
            {
                var length = count >= BufferPool.PacketSize - _currentOffset ? BufferPool.PacketSize - _currentOffset : count;
                Buffer.BlockCopy(buffer.Array, offset, segment.Buffer, segment.Offset + _currentOffset, length);
                _currentOffset += length;

                if (_currentOffset == BufferPool.PacketSize)
                {
                    segment.RioBuffer.Length = BufferPool.PacketSize;
                    _sendCount++;
                    if (!_rio.Send(_requestQueue, &segment.RioBuffer, 1, (((_sendCount & SendMask) == 0) ? MessageEnd : MessagePart), -_sendCount - 1))
                    {
                        ReportError("Send");
                    }
                    _currentOffset = 0;
                    segment = _sendSegments[_sendCount & SendMask];
                }
                else if (_currentOffset > BufferPool.PacketSize)
                {
                    throw new Exception("Overflowed buffer");
                }

                offset += length;
                count -= length;
            } while (count > 0);

            if (isEnd)
            {
                if (_currentOffset > 0)
                {
                    segment.RioBuffer.Length = (uint)_currentOffset;
                    if (!_rio.Send(_requestQueue, &segment.RioBuffer, 1, MessageEnd, -_sendCount - 1))
                    {
                        ReportError("Send");
                        return;
                    }
                    _currentOffset = 0;
                    _sendCount++;
                }
                else
                {
                    if (!_rio.Send(_requestQueue, null, 0, RioSendFlags.CommitOnly, 0))
                    {
                        ReportError("Commit");
                        return;
                    }
                    _currentOffset = 0;
                    _sendCount++;
                }
            }
        }

        private static void ReportError(string type)
        {
            var errorNo = RioImports.WSAGetLastError();

            string errorMessage;
            switch (errorNo)
            {
                case 10014: // WSAEFAULT
                    errorMessage = type + " failed: WSAEFAULT - The system detected an invalid pointer address in attempting to use a pointer argument in a call.";
                    break;
                case 10022: // WSAEINVAL
                    errorMessage = type + " failed: WSAEINVAL -  the SocketQueue parameter is not valid, the Flags parameter contains an value not valid for a send operation, or the integrity of the completion queue has been compromised.";
                    break;
                case 10055: // WSAENOBUFS
                    errorMessage = type + " failed: WSAENOBUFS - Sufficient memory could not be allocated, the I/O completion queue associated with the SocketQueue parameter is full.";
                    break;
                case 997: // WSA_IO_PENDING
                    errorMessage = type + " failed? WSA_IO_PENDING - The operation has been successfully initiated and the completion will be queued at a later time.";
                    break;
                case 995: // WSA_OPERATION_ABORTED
                    errorMessage = type + " failed. WSA_OPERATION_ABORTED - The operation has been canceled while the receive operation was pending. .";
                    break;
                default:
                    errorMessage = string.Format(type + " failed:  WSA error code {0}", errorNo);
                    break;
            }
            throw new InvalidOperationException(errorMessage);

        }

        public void CompleteReceive(long requestCorrelation, uint bytesTransferred)
        {
            var receiveIndex = requestCorrelation & ReceiveMask;
            var receiveTask = _receiveTasks[receiveIndex];
            receiveTask.Complete(bytesTransferred, (uint)receiveIndex);
        }

        internal void PostReceive(long receiveIndex)
        {
            var receiveTask = _receiveTasks[receiveIndex];
            if (!_rio.RioReceive(_requestQueue, ref receiveTask._segment.RioBuffer, 1, RioReceiveFlags.None, receiveIndex))
            {
                ReportError("Receive");
                return;
            }
        }

        public void Close()
        {
            Dispose(true);
        }

        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_receiveTask.Dispose();
                }

                RioTcpConnection connection;
                _rioThread.Connections.TryRemove(_connectionId, out connection);
                RioImports.closesocket(_socket);
                for (var i = 0; i < _receiveTasks.Length; i++)
                {
                    _receiveTasks[i].Dispose();
                }

                for (var i = 0; i < _sendSegments.Length; i++)
                {
                    _sendSegments[i].Dispose();
                }

                disposedValue = true;
            }
        }

        ~RioTcpConnection()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

}
