using Channels.Networking.Sockets.Internal;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Channels.Networking.Sockets
{
    public class SocketConnection : IChannel
    {
        private static readonly EventHandler<SocketAsyncEventArgs> _asyncCompleted = OnAsyncCompleted;
        // try with a trivial "pool" at first
        private static SocketAsyncEventArgs spare;

        private readonly bool _ownsChannelFactory;
        private ChannelFactory _channelFactory;
        private Channel _input, _output;
        private Socket _socket;

        internal SocketConnection(Socket socket, ChannelFactory channelFactory)
        {
            socket.NoDelay = true;
            _socket = socket;
            if (channelFactory == null)
            {
                _ownsChannelFactory = true;
                channelFactory = new ChannelFactory();
            }
            _channelFactory = channelFactory;

            _input = ChannelFactory.CreateChannel();
            _output = ChannelFactory.CreateChannel();

            ProcessReads();
            ProcessWrites();
        }

        public IReadableChannel Input => _input;

        public IWritableChannel Output => _output;

        private ChannelFactory ChannelFactory => _channelFactory;

        private Socket Socket => _socket;

        public static Task<SocketConnection> ConnectAsync(IPEndPoint endPoint, ChannelFactory channelFactory = null)
        {
            var args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = endPoint;
            args.Completed += _asyncCompleted;
            var tcs = new TaskCompletionSource<SocketConnection>(channelFactory);
            args.UserToken = tcs;
            if (!Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, args))
            {
                OnConnect(args); // completed sync - usually means failure
            }
            return tcs.Task;
        }

        public void Dispose() => Dispose(true);

        internal static SocketAsyncEventArgs GetOrCreateSocketAsyncEventArgs()
        {
            var obj = Interlocked.Exchange(ref spare, null);
            if (obj == null)
            {
                obj = new SocketAsyncEventArgs();
                obj.UserToken = new Signal();
                obj.Completed += _asyncCompleted; // only for new, otherwise multi-fire
            }
            else
            {
                ((Signal)obj.UserToken).Reset();
            }
            return obj;
        }

        internal static void RecycleSocketAsyncEventArgs(SocketAsyncEventArgs args)
        {
            if (args != null)
            {
                Interlocked.Exchange(ref spare, args);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                GC.SuppressFinalize(this);
                _socket?.Dispose();
                _socket = null;
                if (_ownsChannelFactory) { _channelFactory?.Dispose(); }
                _channelFactory = null;
            }
        }

        private static void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Connect:
                        OnConnect(e);
                        break;

                    case SocketAsyncOperation.Send:
                    case SocketAsyncOperation.Receive:
                        ReleasePending(e);
                        break;
                }
            }
            catch { }
        }

        private static void OnConnect(SocketAsyncEventArgs e)
        {
            var tcs = (TaskCompletionSource<SocketConnection>)e.UserToken;
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    tcs.TrySetResult(new SocketConnection(e.ConnectSocket, (ChannelFactory)tcs.Task.AsyncState));
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private static void ReleasePending(SocketAsyncEventArgs e)
        {
            var pending = (Signal)e.UserToken;
            pending.Set();
        }

        private async void ProcessReads()
        {
            SocketAsyncEventArgs args = null;
            try
            {
                // if the consumer says they don't want the data, we need to shut down the receive
                GC.KeepAlive(_input.ReaderCompleted.ContinueWith(delegate
                {// GC.KeepAlive here just to shut the compiler up
                    try { Socket.Shutdown(SocketShutdown.Receive); } catch { }
                }));

                // wait for someone to be interested in data before we
                // start allocating buffers and probing the socket
                await _input.ReadingStarted;

                args = GetOrCreateSocketAsyncEventArgs();
                while (!_input.ReaderCompleted.IsCompleted)
                {
                    // we need a buffer to read into
                    var buffer = _input.Alloc(2048); // TODO: should this be controllable by the consumer?
                    bool flushed = false;
                    try
                    {
                        SetBuffer(buffer.Memory, args);
                        if (Socket.ReceiveAsync(args)) //  initiator calls ReceiveAsync
                        {
                            // wait async for the io work to be completed
                            await ((Signal)args.UserToken).WaitAsync();
                        }
                        else
                        {
                            // if ReceiveAsync returns sync, we have the conch - nothing to do - we already received
                        }
                        // either way, need to validate
                        if (args.SocketError != SocketError.Success)
                        {
                            throw new SocketException((int)args.SocketError);
                        }
                        int len = args.BytesTransferred;
                        if (len <= 0)
                        {
                            // end of the socket
                            break;
                        }
                        buffer.CommitBytes(len);
                        await buffer.FlushAsync();
                        flushed = true;
                    }
                    finally
                    {
                        if (!flushed)
                        {
                            await buffer.FlushAsync();
                        }
                    }
                }
                _input.CompleteWriting();
            }
            catch (Exception ex)
            {
                _input?.CompleteWriting(ex);
            }
            finally
            {
                try // we're not going to be reading anything else
                {
                    Socket.Shutdown(SocketShutdown.Receive);
                }
                catch { }
                RecycleSocketAsyncEventArgs(args);
            }
        }

        private async void ProcessWrites()
        {
            SocketAsyncEventArgs args = null;
            try
            {
                args = GetOrCreateSocketAsyncEventArgs();

                while (true)
                {
                    var buffer = await _output.ReadAsync();
                    try
                    {
                        if (buffer.IsEmpty && _output.WriterCompleted.IsCompleted)
                        {
                            break;
                        }
                        foreach (var span in buffer)
                        {
                            SetBuffer(span, args);

                            if (Socket.SendAsync(args)) //  initiator calls SendAsync
                            {
                                // wait async for the semaphore to be released by the callback
                                await ((Signal)args.UserToken).WaitAsync();
                            }
                            else
                            {
                                // if SendAsync returns sync, we have the conch - nothing to do - we already sent
                            }
                            // either way, need to validate
                            if (args.SocketError != SocketError.Success)
                            {
                                throw new SocketException((int)args.SocketError);
                            }
                            if (args.BytesTransferred != span.Length)
                            {
                                throw new NotImplementedException("We didn't send everything; oops!");
                            }
                        }
                    }
                    finally
                    {
                        buffer.Consumed();
                    }
                }
                _output.CompleteReading();
            }
            catch (Exception ex)
            {
                _output?.CompleteReading(ex);
            }
            finally
            {
                try // we're not going to be sending anything else
                {
                    Socket.Shutdown(SocketShutdown.Send);
                }
                catch { }
                RecycleSocketAsyncEventArgs(args);
            }
        }

        // unsafe+async not good friends
        private unsafe void SetBuffer(Span<byte> span, SocketAsyncEventArgs args)
        {
            ArraySegment<byte> segment;
            if (!span.TryGetArray(default(void*), out segment))
            {
                throw new InvalidOperationException("Memory is not backed by an array; oops!");
            }
            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
        }

        private void Shutdown()
        {
            Socket?.Shutdown(SocketShutdown.Both);
        }
    }
}