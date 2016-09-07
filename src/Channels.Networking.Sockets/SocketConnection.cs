using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Channels.Networking.Sockets
{
    public class SocketConnection : IDisposable
    {
        private Socket _socket;
        private Channel _input, _output;
        private ChannelFactory _channelFactory;
        private Task _writes;
        private Task _reads;

        private static readonly EventHandler<SocketAsyncEventArgs> _asyncCompleted = OnAsyncCompleted;

        public IReadableChannel Input => _input;
        public IWritableChannel Output => _output;

        private Socket Socket => _socket;
        private ChannelFactory ChannelFactory => _channelFactory;

        internal SocketConnection(Socket socket, ChannelFactory channelFactory)
        {
            socket.NoDelay = true;
            _socket = socket;

            _channelFactory = channelFactory;

            _input = ChannelFactory.CreateChannel();
            _output = ChannelFactory.CreateChannel();

            _writes = ProcessWrites();
            _reads = ProcessReads();
        }

        // try with a trivial pool at first
        private static SocketAsyncEventArgs spare;

        internal static SocketAsyncEventArgs GetOrCreateSocketAsyncEventArgs()
        {
            var obj = Interlocked.Exchange(ref spare, null);
            if (obj == null)
            {
                obj = new SocketAsyncEventArgs();
                obj.Completed += _asyncCompleted; // only for new, otherwise multi-fire
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

        private async Task ProcessReads()
        {
            Console.WriteLine("Started processing reads");

            SocketAsyncEventArgs args = null;
            try
            {
                // if the consumer says they don't want the data, we need to shut down the receive
                GC.KeepAlive(_input.ReaderCompleted.ContinueWith(delegate
                {
                    // GC.KeepAlive here just to shut the compiler up
                    try
                    {
                        Socket.Shutdown(SocketShutdown.Receive);
                    }
                    catch
                    {
                    }
                }));

                args = GetOrCreateSocketAsyncEventArgs();
                var pending = new SemaphoreSlim(0, 1);
                args.UserToken = pending;

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
                            // wait async for the semaphore to be released by the callback
                            await pending.WaitAsync();
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
            }
            catch (Exception ex)
            {
                _input.CompleteWriting(ex);
            }
            finally
            {
                _input.CompleteWriting();

                Console.WriteLine("Done Processing reads");

                try // we're not going to be reading anything else
                {
                    Socket.Shutdown(SocketShutdown.Receive);
                }
                catch
                {
                }

                RecycleSocketAsyncEventArgs(args);
            }
        }

        private async Task ProcessWrites()
        {
            Console.WriteLine("Started processing writes");

            SocketAsyncEventArgs args = null;

            try
            {
                args = GetOrCreateSocketAsyncEventArgs();
                var pending = new SemaphoreSlim(0, 1);
                args.Completed += _asyncCompleted;
                args.UserToken = pending;

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
                                await pending.WaitAsync();
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
            }
            catch (Exception ex)
            {
                _output.CompleteReading(ex);
            }
            finally
            {
                Console.WriteLine("Done Processing writes");
                _output.CompleteReading();

                try // we're not going to be sending anything else
                {
                    Socket.Shutdown(SocketShutdown.Send);
                }
                catch
                {
                }

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

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Console.WriteLine("Disposing the socket");
                _writes.Wait();
                _reads.Wait();

                GC.SuppressFinalize(this);
                _socket?.Dispose();
                _socket = null;
                _channelFactory = null;
            }
        }

        private void Shutdown()
        {
            Socket?.Shutdown(SocketShutdown.Both);
        }

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
            catch
            {
            }
        }

        private static void ReleasePending(SocketAsyncEventArgs e)
        {
            var pending = (SemaphoreSlim)e.UserToken;
            pending.Release();
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
    }
}
