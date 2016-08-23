// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Channels.Samples.Internal;
using Channels.Samples.Internal.Winsock;

namespace Channels.Samples
{
    public sealed class RioTcpConnection : IDisposable
    {
        private readonly static Task _completedTask = Task.FromResult(0);

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

        private const long PartialSendCorrelation = -1;
        private const long RestartSendCorrelations = -2;
        private long _sendCorrelation = RestartSendCorrelations;

        private readonly SingleConsumerSemaphore _outgoingSends = new SingleConsumerSemaphore(RioTcpServer.MaxWritesPerSocket);
        private readonly SingleConsumerSemaphore _sendsComplete = new SingleConsumerSemaphore(0);

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

        private long CompleteSendCorrelation()
        {
            var sendCorrelation = _sendCorrelation;
            if (sendCorrelation == int.MinValue)
            {
                _sendCorrelation = RestartSendCorrelations;
                return RestartSendCorrelations;
            }

            _sendCorrelation = sendCorrelation - 1;
            return sendCorrelation - 1;
        }

        private void MarkReadyToSend(long correlation)
        {
            _outgoingSends.Release();

            if (correlation == _sendCorrelation)
            {
                _sendsComplete.Release();
            }
        }

        private void ProcessReceives()
        {
            _buffer = Input.Alloc(2048);
            var receiveBufferSeg = GetSegmentFromSpan(_buffer.Memory);

            if (!_rio.RioReceive(_requestQueue, ref receiveBufferSeg, 1, RioReceiveFlags.None, 0))
            {
                ThrowError(ErrorType.Receive);
            }
        }

        private async Task ProcessSends()
        {
            while (true)
            {
                await Output;

                var buffer = Output.BeginRead();

                if (buffer.Length == 0 && Output.Completion.IsCompleted)
                {
                    break;
                }

                var enumerator = buffer.GetSpans().GetEnumerator();

                if (enumerator.MoveNext())
                {
                    var current = enumerator.Current;

                    while (enumerator.MoveNext())
                    {
                        var next = enumerator.Current;

                        await SendAsync(current, false);
                        current = next;
                    }

                    await SendAsync(current, true);
                }

                await _sendsComplete;

                Output.EndRead(buffer);
            }

            Output.CompleteReading();
        }

        private Task SendAsync(BufferSpan span, bool endOfMessage)
        {
            int remaining;
            if (!_outgoingSends.TryAcquire(out remaining))
            {
                return SendAsyncAwaited(span, endOfMessage);
            }

            var flushSends = endOfMessage || remaining == 0;

            Send(GetSegmentFromSpan(span), flushSends);

            if (flushSends && !endOfMessage)
            {
               return AwaitSendComplete();
            }

            return _completedTask;
        }

        private async Task SendAsyncAwaited(BufferSpan span, bool endOfMessage)
        {
            var remaining = await _outgoingSends;

            var flushSends = endOfMessage || remaining == 0;

            Send(GetSegmentFromSpan(span), flushSends);

            if (flushSends && !endOfMessage)
            {
                await _outgoingSends;
            }
        }

        private async Task AwaitSendComplete()
        {
            await _outgoingSends;
        }

        private void Send(RioBufferSegment segment, bool flushSends)
        {
            var sendCorrelation = flushSends ? CompleteSendCorrelation() : PartialSendCorrelation;
            var sendFlags = flushSends ? MessageEnd : MessagePart;

            if (!_rio.Send(_requestQueue, ref segment, 1, sendFlags, sendCorrelation))
            {
                ThrowError(ErrorType.Send);
            }
        }

        private static RioBufferSegment GetSegmentFromSpan(BufferSpan span)
        {
            var bufferId = (IntPtr)span.UserData;
            return new RioBufferSegment(bufferId, (uint)span.Offset, (uint)span.Length);
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
