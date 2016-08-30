// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Channels.Networking.Windows.RIO.Internal;
using Channels.Networking.Windows.RIO.Internal.Winsock;

namespace Channels.Networking.Windows.RIO
{
    public sealed class RioTcpConnection : IDisposable
    {
        private const RioSendFlags MessagePart = RioSendFlags.Defer | RioSendFlags.DontNotify;
        private const RioSendFlags MessageEnd = RioSendFlags.None;

        private const long PartialSendCorrelation = -1;
        private const long RestartSendCorrelations = -2;

        private readonly static Task _completedTask = Task.FromResult(0);

        private readonly long _connectionId;
        private readonly IntPtr _socket;
        private readonly IntPtr _requestQueue;
        private readonly RegisteredIO _rio;
        private readonly RioThread _rioThread;
        private bool _disposedValue;

        private long _previousSendCorrelation = RestartSendCorrelations;

        private readonly MemoryPoolChannel _input;
        private readonly MemoryPoolChannel _output;

        private readonly SemaphoreSlim _outgoingSends = new SemaphoreSlim(RioTcpServer.MaxWritesPerSocket);
        private readonly SemaphoreSlim _previousSendsComplete = new SemaphoreSlim(1);

        private Task _sendTask;

        private ReadableBuffer _sendingBuffer;
        private WritableBuffer _buffer;

        internal RioTcpConnection(IntPtr socket, long connectionId, IntPtr requestQueue, RioThread rioThread, RegisteredIO rio)
        {
            _socket = socket;
            _connectionId = connectionId;
            _rio = rio;
            _rioThread = rioThread;

            _input = rioThread.ChannelFactory.CreateChannel();
            _output = rioThread.ChannelFactory.CreateChannel();

            _requestQueue = requestQueue;

            rioThread.Connections.TryAdd(connectionId, this);

            ProcessReceives();
            _sendTask = ProcessSends();
        }

        public IReadableChannel Input => _input;
        public IWritableChannel Output => _output;

        private void ProcessReceives()
        {
            _buffer = _input.Alloc(2048);
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
                await _output;

                var buffer = _output.Read();

                if (buffer.IsEmpty && _output.Completion.IsCompleted)
                {
                    break;
                }

                var enumerator = buffer.GetEnumerator();

                if (enumerator.MoveNext())
                {
                    var current = enumerator.Current;

                    while (enumerator.MoveNext())
                    {
                        var next = enumerator.Current;

                        await SendAsync(current, endOfMessage: false);
                        current = next;
                    }

                    await PreviousSendingComplete;

                    _sendingBuffer = buffer.Clone();

                    await SendAsync(current, endOfMessage: true);
                }

                buffer.Consumed();
            }

            _output.CompleteReading();
        }

        private Task SendAsync(BufferSpan span, bool endOfMessage)
        {
            if (!IsReadyToSend)
            {
                return SendAsyncAwaited(span, endOfMessage);
            }

            var flushSends = endOfMessage || MaxOutstandingSendsReached;

            Send(GetSegmentFromSpan(span), flushSends);

            if (flushSends && !endOfMessage)
            {
                return AwaitReadyToSend();
            }

            return _completedTask;
        }

        private async Task SendAsyncAwaited(BufferSpan span, bool endOfMessage)
        {
            await ReadyToSend;

            var flushSends = endOfMessage || MaxOutstandingSendsReached;

            Send(GetSegmentFromSpan(span), flushSends);

            if (flushSends && !endOfMessage)
            {
                await ReadyToSend;
            }
        }

        private async Task AwaitReadyToSend()
        {
            await ReadyToSend;
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

        private Task PreviousSendingComplete => _previousSendsComplete.WaitAsync();

        private Task ReadyToSend => _outgoingSends.WaitAsync();

        private bool IsReadyToSend => _outgoingSends.Wait(0);

        private bool MaxOutstandingSendsReached => _outgoingSends.CurrentCount == 0;

        private void CompleteSend() => _outgoingSends.Release();

        private void CompletePreviousSending()
        {
            _sendingBuffer.Dispose();
            _previousSendsComplete.Release();
        }

        private long CompleteSendCorrelation()
        {
            var sendCorrelation = _previousSendCorrelation;
            if (sendCorrelation == int.MinValue)
            {
                _previousSendCorrelation = RestartSendCorrelations;
                return RestartSendCorrelations;
            }

            _previousSendCorrelation = sendCorrelation - 1;
            return sendCorrelation - 1;
        }

        private void SendCompleting(long sendCorrelation)
        {
            CompleteSend();

            if (sendCorrelation == _previousSendCorrelation)
            {
                CompletePreviousSending();
            }
        }

        private RioBufferSegment GetSegmentFromSpan(BufferSpan span)
        {
            var bufferId = _rioThread.GetBufferId(span.BufferBasePtr);
            return new RioBufferSegment(bufferId, (uint)span.Offset, (uint)span.Length);
        }

        public void Complete(int status, long requestCorrelation, uint bytesTransferred)
        {
            // Receives
            if (requestCorrelation >= 0)
            {
                if (bytesTransferred > 0)
                {
                    _buffer.UpdateWritten((int)bytesTransferred);
                    _input.WriteAsync(_buffer);

                    ProcessReceives();
                }
                else
                {
                    _input.CompleteWriting();
                }
            }
            else
            {
                SendCompleting(requestCorrelation);
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
