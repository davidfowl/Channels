// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Channels;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public sealed class RIOTcpConnection : IDisposable
    {
        long _connectionId;
        IntPtr _socket;
        IntPtr _requestQueue;
        RIO _rio;
        RIOThread _thread;

        long _sendCount = 0;
        long _receiveRequestCount = 0;

        RIOReceiveTask[] _receiveTasks;
        RIOPooledSegment[] _sendSegments;
        public const int MaxPendingReceives = 16;
        public const int MaxPendingSends = MaxPendingReceives * 2;
        public const int IOCPOverflowEvents = 8;
        const int ReceiveMask = MaxPendingReceives - 1;
        const int SendMask = MaxPendingSends - 1;

        public MemoryPoolChannel Input { get; }
        public MemoryPoolChannel Output { get; }
        public ChannelFactory ChannelFactory => _thread.channelFactory;

        internal RIOTcpConnection(IntPtr socket, long connectionId, RIOThread thread, RIO rio)
        {
            _socket = socket;
            _connectionId = connectionId;
            _rio = rio;
            _thread = thread;

            Input = ChannelFactory.CreateChannel();
            Output = ChannelFactory.CreateChannel();

            _requestQueue = _rio.CreateRequestQueue(_socket, MaxPendingReceives + IOCPOverflowEvents, 1, MaxPendingSends + IOCPOverflowEvents, 1, thread.completionQueue, thread.completionQueue, connectionId);
            if (_requestQueue == IntPtr.Zero)
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(String.Format("ERROR: CreateRequestQueue returned {0}", error));
            }

            _receiveTasks = new RIOReceiveTask[MaxPendingReceives];

            for (var i = 0; i < _receiveTasks.Length; i++)
            {
                _receiveTasks[i] = new RIOReceiveTask(this, thread.bufferPool.GetBuffer());
            }

            _sendSegments = new RIOPooledSegment[MaxPendingSends];
            for (var i = 0; i < _sendSegments.Length; i++)
            {
                _sendSegments[i] = thread.bufferPool.GetBuffer();
            }

            thread.connections.TryAdd(connectionId, this);

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
                    QueueSend(span.Buffer, true);
                }

                Output.EndRead(begin);
            }
        }

        const RIO_SEND_FLAGS MessagePart = RIO_SEND_FLAGS.DEFER | RIO_SEND_FLAGS.DONT_NOTIFY;
        const RIO_SEND_FLAGS MessageEnd = RIO_SEND_FLAGS.NONE;

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
                var length = count >= RIOBufferPool.PacketSize - _currentOffset ? RIOBufferPool.PacketSize - _currentOffset : count;
                Buffer.BlockCopy(buffer.Array, offset, segment.Buffer, segment.Offset + _currentOffset, length);
                _currentOffset += length;

                if (_currentOffset == RIOBufferPool.PacketSize)
                {
                    segment.RioBuffer.Length = RIOBufferPool.PacketSize;
                    _sendCount++;
                    if (!_rio.Send(_requestQueue, &segment.RioBuffer, 1, (((_sendCount & SendMask) == 0) ? MessageEnd : MessagePart), -_sendCount - 1))
                    {
                        ReportError("Send");
                    }
                    _currentOffset = 0;
                    segment = _sendSegments[_sendCount & SendMask];
                }
                else if (_currentOffset > RIOBufferPool.PacketSize)
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
                    if (!_rio.Send(_requestQueue, null, 0, RIO_SEND_FLAGS.COMMIT_ONLY, 0))
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
            var errorNo = RIOImports.WSAGetLastError();

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

        public void CompleteReceive(long RequestCorrelation, uint BytesTransferred)
        {
            var receiveIndex = RequestCorrelation & ReceiveMask;
            var receiveTask = _receiveTasks[receiveIndex];
            receiveTask.Complete(BytesTransferred, (uint)receiveIndex);
        }

        internal void PostReceive(long receiveIndex)
        {
            var receiveTask = _receiveTasks[receiveIndex];
            if (!_rio.Receive(_requestQueue, ref receiveTask._segment.RioBuffer, 1, RIO_RECEIVE_FLAGS.NONE, receiveIndex))
            {
                ReportError("Receive");
                return;
            }
        }

        public void Close()
        {
            Dispose(true);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_receiveTask.Dispose();
                }

                RIOTcpConnection connection;
                _thread.connections.TryRemove(_connectionId, out connection);
                RIOImports.closesocket(_socket);
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

        ~RIOTcpConnection()
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
        #endregion

    }

}
