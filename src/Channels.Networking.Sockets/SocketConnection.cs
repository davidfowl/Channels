using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Channels.Networking.Sockets
{
    public class SocketConnection : IDisposable
    {
        private Socket socket;
        private Channel input, output;

        public IReadableChannel Input => input;
        public IWritableChannel Output => output;
        private ChannelFactory ownedChannelFactory;

        internal SocketConnection(Socket socket, ChannelFactory channelFactory)
        {
            socket.NoDelay = true;
            this.socket = socket;
            if(channelFactory == null)
            {
                ownedChannelFactory = channelFactory = new ChannelFactory();
            }
            input = channelFactory.CreateChannel();
            output = channelFactory.CreateChannel();

            ProcessReads();
            ProcessWrites();

        }
        private async void ProcessReads()
        {
            try
            {
                var args = new SocketAsyncEventArgs();
                SemaphoreSlim pending = new SemaphoreSlim(0, 1);
                args.Completed += AsyncCompleted;
                args.UserToken = pending;

                while (true)
                {
                    // we need a buffer to read into
                    var buffer = input.Alloc(2048);
                    SetBuffer(buffer.Memory, args);

                    if (socket.ReceiveAsync(args)) //  initiator calls ReceiveAsync
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
                    if(len <= 0)
                    {
                        // end of the socket
                        await buffer.FlushAsync();
                        break;
                    }
                    
                    buffer.CommitBytes(len);
                    await buffer.FlushAsync();
                }
                input.CompleteWriting();
            }
            catch (Exception ex)
            {
                input?.CompleteWriting(ex);
            }
            finally
            {
                try // we're not going to be reading anything else
                {
                    socket.Shutdown(SocketShutdown.Receive);
                }
                catch { }
            }
        }
        private async void ProcessWrites()
        {
            try
            {
                var args = new SocketAsyncEventArgs();
                SemaphoreSlim pending = new SemaphoreSlim(0, 1);
                args.Completed += AsyncCompleted;
                args.UserToken = pending;

                while (true)
                {

                    var buffer = await output.ReadAsync();
                    try
                    {
                        if (buffer.IsEmpty && output.WriterCompleted.IsCompleted)
                        {
                            break;
                        }
                        foreach (var span in buffer)
                        {
                            SetBuffer(span, args);

                            if (socket.SendAsync(args)) //  initiator calls SendAsync
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
                output.CompleteReading();
            }
            catch (Exception ex)
            {
                output?.CompleteReading(ex);
            }
            finally
            {
                try // we're not going to be sending anything else
                {
                    socket.Shutdown(SocketShutdown.Send);
                }
                catch { }
            }
        }

        // unsafe+async not good friends
        private unsafe void SetBuffer(Span<byte> span, SocketAsyncEventArgs args)
        {
            ArraySegment<byte> segment;
            if(!span.TryGetArray(default(void*), out segment))
            {
                throw new InvalidOperationException("Memory is not backed by an array; oops!");
            }
            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
        }

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                GC.SuppressFinalize(this);
                socket?.Dispose();
                socket = null;
                ownedChannelFactory?.Dispose();
                ownedChannelFactory = null;                
            }
        }
        private void Shutdown()
        {
            socket?.Shutdown(SocketShutdown.Both);
        }
        public static Task<SocketConnection> ConnectAsync(IPEndPoint endPoint, ChannelFactory channelFactory = null)
        {
            var args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = endPoint;
            args.Completed += AsyncCompleted;
            var tcs = new TaskCompletionSource<SocketConnection>(channelFactory);
            args.UserToken = tcs;
            if (!Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, args))
            {
                OnConnect(args); // completed sync - usually means failure
            }
            return tcs.Task;
        }

        static readonly EventHandler<SocketAsyncEventArgs> AsyncCompleted = (sender, args)
            => OnAsyncCompleted(sender, args);

        
        private static void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                switch(e.LastOperation)
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
            } catch(Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }
    }
}
