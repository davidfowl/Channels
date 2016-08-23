// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
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
        private bool _disposedValue;

        private const RioSendFlags MessagePart = RioSendFlags.Defer | RioSendFlags.DontNotify;
        private const RioSendFlags MessageEnd = RioSendFlags.None;

        public MemoryPoolChannel Input { get; }
        public MemoryPoolChannel Output { get; }
        public ChannelFactory ChannelFactory => _rioThread.ChannelFactory;

        private WritableBuffer _buffer;
        private RioBufferSegment _receiveBufferSeg;
        private RioBufferSegment _sendBufferSeg;

        private long _lastSendCorrelation = -1;
        private readonly SemaphoreSlim _sendsComplete = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _outgoingSends = new SemaphoreSlim(RioTcpServer.MaxWritesPerSocket);

        private Task _sendTask;

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
            _sendTask = ProcessSends();
        }

        private Task SendingComplete => _sendsComplete.WaitAsync();

        private Task ReadyToSend => _outgoingSends.WaitAsync();

        private long PartialSendCorrelation() => _lastSendCorrelation;

        private long FinalSendCorrelation() => (_lastSendCorrelation = _lastSendCorrelation == int.MinValue ? -1 : _lastSendCorrelation - 1);

        private void MarkReadyToSend(long correlation)
        {
            _outgoingSends.Release();

            if (correlation == _lastSendCorrelation)
            {
                _sendsComplete.Release();
            }
        }

        private void ProcessReceives()
        {
            _buffer = Input.Alloc(2048);
            _receiveBufferSeg = GetSegmentFromSpan(_buffer.Memory);

            if (!_rio.RioReceive(_requestQueue, ref _receiveBufferSeg, 1, RioReceiveFlags.None, 0))
            {
                ThrowError(ErrorType.Receive);
            }
        }

        private async Task ProcessSends()
        {
            while (true)
            {
                await Output;

                var current = Output.BeginRead();

                if (current.IsEnd && Output.Completion.IsCompleted)
                {
                    break;
                }

                BufferSpan last;
                if (current.TryGetBuffer(out last))
                {
                    BufferSpan span;
                    while (current.TryGetBuffer(out span))
                    {
                        await ReadyToSend;

                        Send(last, MessagePart, PartialSendCorrelation());
                        last = span;
                    }

                    await ReadyToSend;

                    Send(last, MessageEnd, FinalSendCorrelation());
                }

                await SendingComplete;

                Output.EndRead(current);
            }

            Output.CompleteReading();
        }

        private void Send(BufferSpan span, RioSendFlags type, long correlation)
        {
            _sendBufferSeg = GetSegmentFromSpan(span);

            if (!_rio.Send(_requestQueue, ref _sendBufferSeg, 1, type, correlation))
            {
                ThrowError(ErrorType.Send);
            }
        }

        private static RioBufferSegment GetSegmentFromSpan(BufferSpan span)
        {
            var bufferId = (IntPtr)span.UserData;
            return new RioBufferSegment(bufferId, (uint)span.Buffer.Offset, (uint)span.Length);
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
                else
                {
                    Input.CompleteWriting();
                }
            }
            else
            {
                // Sends
                MarkReadyToSend(requestCorrelation);
            }
        }

        private static void ThrowError(ErrorType type)
        {
            var errorNo = RioImports.WSAGetLastError();

            string errorMessage;
            switch (errorNo)
            {
                case 10014: // WSAEFAULT
                    errorMessage = $"{type} failed: WSAEFAULT - The system detected an invalid pointer address in attempting to use a pointer argument in a call.";
                    break;
                case 10022: // WSAEINVAL
                    errorMessage = $"{type} failed: WSAEINVAL -  the SocketQueue parameter is not valid, the Flags parameter contains an value not valid for a send operation, or the integrity of the completion queue has been compromised.";
                    break;
                case 10055: // WSAENOBUFS
                    errorMessage = $"{type} failed: WSAENOBUFS - Sufficient memory could not be allocated, the I/O completion queue associated with the SocketQueue parameter is full.";
                    break;
                case 997: // WSA_IO_PENDING
                    errorMessage = $"{type} failed? WSA_IO_PENDING - The operation has been successfully initiated and the completion will be queued at a later time.";
                    break;
                case 995: // WSA_OPERATION_ABORTED
                    errorMessage = $"{type} failed. WSA_OPERATION_ABORTED - The operation has been canceled while the receive operation was pending.";
                    break;
                default:
                    errorMessage = $"{type} failed:  WSA error code {errorNo}";
                    break;
            }

            throw new InvalidOperationException(errorMessage);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                RioTcpConnection connection;
                _rioThread.Connections.TryRemove(_connectionId, out connection);
                RioImports.closesocket(_socket);

                _disposedValue = true;
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

        private enum ErrorType
        {
            Send,
            Receive
        }
    }
}
