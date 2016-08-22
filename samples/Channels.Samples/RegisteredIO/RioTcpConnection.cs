// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Channels.Samples.Internal;
using Channels.Samples.Internal.Winsock;

namespace Channels.Samples
{
    public sealed class RioTcpConnection : IDisposable
    {
        private readonly long _connectionId;
        private readonly IntPtr _socket;
        private readonly IntPtr _requestQueue;
        private readonly RegisteredIO _rio;
        private readonly RioThread _rioThread;

        private const RioSendFlags MessagePart = RioSendFlags.Defer | RioSendFlags.DontNotify;
        private const RioSendFlags MessageEnd = RioSendFlags.None;

        public MemoryPoolChannel Input { get; }
        public MemoryPoolChannel Output { get; }
        public ChannelFactory ChannelFactory => _rioThread.ChannelFactory;

        private WritableBuffer _buffer;
        private BufferSegment _bufferSeg;

        internal RioTcpConnection(IntPtr socket, long connectionId, IntPtr requestQueue, RioThread rioThread, RegisteredIO rio)
        {
            _socket = socket;
            _connectionId = connectionId;
            _rio = rio;
            _rioThread = rioThread;

            Input = ChannelFactory.CreateChannel();
            Output = ChannelFactory.CreateChannel();

            _requestQueue = requestQueue;

            rioThread.Connections.TryAdd(connectionId, this);

            ProcessReceives();
            ProcessSends();
        }

        private void ProcessReceives()
        {
            _buffer = Input.Alloc(2048);
            _bufferSeg = GetSegmentFromSpan(_buffer.Memory);

            if (!_rio.RioReceive(_requestQueue, ref _bufferSeg, 1, RioReceiveFlags.None, 0))
            {
                ReportError("Receive");
                return;
            }
        }

        private static BufferSegment GetSegmentFromSpan(BufferSpan span)
        {
            var bufferId = (IntPtr)span.UserData;
            return new BufferSegment(bufferId, (uint)span.Buffer.Offset, (uint)span.Length);
        }

        public void Complete(long requestCorrelation, uint bytesTransferred)
        {
            // Receives
            if (requestCorrelation >= 0)
            {
                if (bytesTransferred > 0)
                {
                    _buffer.UpdateWritten((int)bytesTransferred);
                    Input.WriteAsync(_buffer);

                    ProcessReceives();
                }
            }
            else
            {
                // Sends
                if (bytesTransferred > 0)
                {

                }
            }
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
                    Send(span);
                }

                Output.EndRead(begin);
            }
        }

        private unsafe void Send(BufferSpan span)
        {
            var segment = GetSegmentFromSpan(span);

            if (!_rio.Send(_requestQueue, &segment, 1, MessageEnd, -1))
            {
                ReportError("Send");
                return;
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

        public void Close()
        {
            Dispose(true);
        }

        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                RioTcpConnection connection;
                _rioThread.Connections.TryRemove(_connectionId, out connection);
                RioImports.closesocket(_socket);

                // _receiveSegment.Dispose();

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
