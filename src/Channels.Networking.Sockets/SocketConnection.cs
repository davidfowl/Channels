using Channels.Networking.Sockets.Internal;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Channels.Networking.Sockets
{
    /// <summary>
    /// Represents a channel implementation using the async Socket API
    /// </summary>
    public class SocketConnection : IChannel
    {
        private static readonly EventHandler<SocketAsyncEventArgs> _asyncCompleted = OnAsyncCompleted;
        // try with a trivial "pool" at first
        private static SocketAsyncEventArgs spare;

        // TODO: consider a better pooling strategy here; small fragments of a larger buffer
        // would be much cheaper, but is it too much tracking overhead? could use a 64*size
        // buffer using a ulong as a bit-vector to track which blocks are in use, for example
        private static byte[] _recycledSmallBuffer;

        // track the state of which strategy to use; need to use a known-safe
        // strategy until we can decide which to use (by observing behavior)
        private static BufferStyle _bufferStyle;
        private static bool _seenReceiveZeroWithAvailable, _seenReceiveZeroWithEOF;

        private static readonly byte[] _zeroLengthBuffer = new byte[0];


        private readonly bool _ownsChannelFactory;
        private ChannelFactory _channelFactory;
        private Channel _input, _output;
        private Socket _socket;

        static SocketConnection()
        {

            // validated styles for known OSes
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // zero-length receive works fine
                _bufferStyle = BufferStyle.UseZeroLengthBuffer;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // zero-length receive is unreliable
                _bufferStyle = BufferStyle.UseSmallBuffer;
            }
            else
            {
                // default to "figure it out based on what happens"
                _bufferStyle = BufferStyle.Unknown;
            }
        }
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

            ShutdownSocketWhenWritingCompletedAsync();
            ReceiveFromSocketAndPushToChannelAsync();
            ReadFromChannelAndWriteToSocketAsync();
        }

        /// <summary>
        /// Provides access to data received from the socket
        /// </summary>
        public IReadableChannel Input => _input;

        /// <summary>
        /// Provides access to write data to the socket
        /// </summary>
        public IWritableChannel Output => _output;

        private ChannelFactory ChannelFactory => _channelFactory;

        private Socket Socket => _socket;

        /// <summary>
        /// Begins an asynchronous connect operation to the designated endpoint
        /// </summary>
        /// <param name="endPoint">The endpoint to which to connect</param>
        /// <param name="channelFactory">Optionally allows the underlying channel factory (and hence memory pool) to be specified; if one is not provided, a channel factory will be instantiated and owned by the connection</param>
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
        /// <summary>
        /// Releases all resources owned by the connection
        /// </summary>
        public void Dispose() => Dispose(true);

        internal static SocketAsyncEventArgs GetOrCreateSocketAsyncEventArgs()
        {
            var obj = Interlocked.Exchange(ref spare, null);
            if (obj == null)
            {
                obj = new SocketAsyncEventArgs();
                obj.Completed += _asyncCompleted; // only for new, otherwise multi-fire
            }
            if (obj.UserToken is Signal)
            {
                ((Signal)obj.UserToken).Reset();
            }
            else
            {
                obj.UserToken = new Signal();
            }
            return obj;
        }

        internal static void RecycleSocketAsyncEventArgs(SocketAsyncEventArgs args)
        {
            if (args != null)
            {
                args.SetBuffer(null, 0, 0); // make sure we don't keep a slab alive
                Interlocked.Exchange(ref spare, args);
            }
        }
        /// <summary>
        /// Releases all resources owned by the connection
        /// </summary>
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

        private enum BufferStyle
        {
            Unknown,
            UseZeroLengthBuffer,
            UseSmallBuffer
        }

        private async void ShutdownSocketWhenWritingCompletedAsync()
        {
            // the intent of this is so that *external* callers can cause the
            // socket to become shut down; a natural consequence is that this
            // will also run if we shut it down from inside, but... that isn't
            // a huge problem
            try
            {
                await _input.Writing;
            }
            catch { } // lots of swallowing here; this is all in crazy conditions
            try
            {
                Socket.Shutdown(SocketShutdown.Receive);
            }
            catch { }
        }
        private async void ReceiveFromSocketAndPushToChannelAsync()
        {
            SocketAsyncEventArgs args = null;
            try
            {
                // if the consumer says they don't want the data, we need to shut down the receive
                GC.KeepAlive(_input.Writing.ContinueWith(delegate
                {// GC.KeepAlive here just to shut the compiler up
                    try { Socket.Shutdown(SocketShutdown.Receive); } catch { }
                }));

                // wait for someone to be interested in data before we
                // start allocating buffers and probing the socket
                await _input.ReadingStarted;

                args = GetOrCreateSocketAsyncEventArgs();
                while (!_input.Writing.IsCompleted)
                {
                    byte[] initialDataBuffer = null;
                    int bytesFromInitialDataBuffer = 0;


                    if (Socket.Available == 0)
                    {
                        // now, this gets a bit messy unfortunately, because support for the ideal option
                        // (zero-length reads) is platform dependent
                        switch (_bufferStyle)
                        {
                            case BufferStyle.Unknown:
                                try
                                {
                                    initialDataBuffer = await ReceiveInitialDataUnknownStrategyAsync(args);
                                }
                                catch (Exception ex)
                                {
                                    initialDataBuffer = null;
                                }
                                if (initialDataBuffer == null)
                                {
                                    continue; // redo from start
                                }
                                break;
                            case BufferStyle.UseZeroLengthBuffer:
                                // if we already have a buffer, use that (but: zero count); otherwise use a shared
                                // zero-length; this avoids constantly changing the buffer that the args use, which
                                // avoids some overheads
                                args.SetBuffer(args.Buffer ?? _zeroLengthBuffer, 0, 0);
                                if (Socket.ReceiveAsync(args))
                                {
                                    // wait async for the io work to be completed
                                    await ((Signal)args.UserToken).WaitAsync();
                                }
                                break;
                            case BufferStyle.UseSmallBuffer:
                                // We need  to do a speculative receive with a *cheap* buffer while we wait for input; it would be *nice* if
                                // we could do a zero-length receive, but this is not supported equally on all platforms (fine on Windows, but
                                // linux hates it). The key aim here is to make sure that we don't tie up an entire block from the memory pool
                                // waiting for input on a socket; fine for 1 socket, not so fine for 100,000 sockets

                                // do a short receive while we wait (async) for data
                                initialDataBuffer = LeaseSmallBuffer();
                                args.SetBuffer(initialDataBuffer, 0, initialDataBuffer.Length);
                                if (Socket.ReceiveAsync(args))
                                {
                                    // wait async for the io work to be completed
                                    await ((Signal)args.UserToken).WaitAsync();
                                }
                                break;
                        }
                        if (args.SocketError != SocketError.Success)
                        {
                            throw new SocketException((int)args.SocketError);
                        }

                        // note we can't check BytesTransferred <= 0, as we always
                        // expect 0; but if we returned, we expect data to be
                        // buffered *on the socket*, else EOF
                        if ((bytesFromInitialDataBuffer = args.BytesTransferred) <= 0)
                        {
                            if ((object)initialDataBuffer == (object)_zeroLengthBuffer)
                            {
                                // sentinel value that means we should just
                                // consume sync (we expect there to be data)
                                initialDataBuffer = null;
                            }
                            else
                            {
                                // socket reported EOF
                                RecycleSmallBuffer(ref initialDataBuffer);
                            }
                            if (Socket.Available == 0)
                            {
                                // yup, definitely an EOF
                                break;
                            }
                        }
                    }


                    // note that we will try to coalesce things here to reduce the number of flushes; we
                    // certainly want to coalesce the initial buffer (from the speculative receive) with the initial
                    // data, but we probably don't want to buffer indefinitely; for now, it will buffer up to 4 pages
                    // before flushing (entirely arbitrarily) - might want to make this configurable later
                    var buffer = _input.Alloc(SmallBufferBytes * 2);
                    const int FlushInputEveryBytes = 4 * MemoryPool.MaxPooledBlockLength;
                    try
                    {
                        if (initialDataBuffer != null)
                        {
                            // need to account for anything that we got in the speculative receive
                            if (bytesFromInitialDataBuffer != 0)
                            {
                                buffer.Write(new Span<byte>(initialDataBuffer, 0, bytesFromInitialDataBuffer));
                            }
                            // make the small buffer available to other consumers
                            RecycleSmallBuffer(ref initialDataBuffer);
                        }

                        bool isEOF = false;
                        while (Socket.Available != 0 && buffer.BytesWritten < FlushInputEveryBytes)
                        {
                            buffer.Ensure(); // ask for *something*, then use whatever is available (usually much much more)
                            SetBuffer(buffer.Memory, args);
                            if (Socket.ReceiveAsync(args)) //  initiator calls ReceiveAsync
                            {
                                // wait async for the io work to be completed
                                await ((Signal)args.UserToken).WaitAsync();
                            }

                            // either way, need to validate
                            if (args.SocketError != SocketError.Success)
                            {
                                throw new SocketException((int)args.SocketError);
                            }
                            int len = args.BytesTransferred;
                            if (len <= 0)
                            {
                                // socket reported EOF
                                isEOF = true;
                                break;
                            }

                            // record what data we filled into the buffer
                            buffer.Advance(len);
                        }
                        if (isEOF)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        await buffer.FlushAsync();
                    }
                }
                _input.CompleteWriter();
            }
            catch (Exception ex)
            {
                // don't trust signal after an error; someone else could
                // still have it and invoke Set
                if (args != null)
                {
                    args.UserToken = null;
                }
                _input?.CompleteWriter(ex);
            }
            finally
            {
                RecycleSocketAsyncEventArgs(args);
            }
        }


        private const int SmallBufferBytes = 8;
        private static byte[] LeaseSmallBuffer()
        {
            return Interlocked.Exchange(ref _recycledSmallBuffer, null) ?? new byte[SmallBufferBytes];
        }
        private void RecycleSmallBuffer(ref byte[] buffer)
        {
            if (buffer != null)
            {
                // this is used as a sentinel in the unknown-strategy code;
                // don't recycle it
                if ((object)buffer != (object)_zeroLengthBuffer)
                {
                    Interlocked.Exchange(ref _recycledSmallBuffer, buffer);
                }
                buffer = null;
            }
        }

        /// returns null if the caller should redo from start; returns
        /// a non-null result to preocess the data
        private async Task<byte[]> ReceiveInitialDataUnknownStrategyAsync(SocketAsyncEventArgs args)
        {

            // to prove that it works OK, we need (after a read):
            // - have seen return 0 and Available > 0
            // - have reen return <= 0 and Available == 0 and is true EOF
            //
            // if we've seen both, we can switch to the simpler approach;
            // until then, if we just see return 0 and Available > 0, well...
            // we're happy
            //
            // note: if we see return 0 and available == 0 and not EOF,
            // then we know that zero-length receive is not supported

            try
            {
                args.SetBuffer(_zeroLengthBuffer, 0, 0);
                // we'll do a receive and see what happens
                if (Socket.ReceiveAsync(args))
                {
                    // wait async for the io work to be completed
                    await ((Signal)args.UserToken).WaitAsync();
                }
            }
            catch
            {
                // well, it didn't like that... switch to small buffers
                _bufferStyle = BufferStyle.UseSmallBuffer;
                return null;
            }
            if (args.SocketError != SocketError.Success)
            {   // let the calling code explode
                return _zeroLengthBuffer;
            }

            if (Socket.Available > 0)
            {
                _seenReceiveZeroWithAvailable = true;
                if (_seenReceiveZeroWithEOF)
                {
                    _bufferStyle = BufferStyle.UseZeroLengthBuffer;
                }
                // we'll let the calling method pull the data out
                return _zeroLengthBuffer;
            }

            // so now we need to detect if this is a genuine EOF; if it isn't,
            // that isn't conclusive, because could just be timing; but if it is: great
            var buffer = LeaseSmallBuffer();
            args.SetBuffer(buffer, 0, buffer.Length);
            // we'll do a receive and see what happens
            if (Socket.ReceiveAsync(args))
            {
                // wait async for the io work to be completed
                await ((Signal)args.UserToken).WaitAsync();
            }
            if (args.SocketError != SocketError.Success)
            {   // we can't actually conclude  anything
                RecycleSmallBuffer(ref buffer);
                throw new SocketException((int)args.SocketError);
            }
            if (args.BytesTransferred <= 0)
            {
                RecycleSmallBuffer(ref buffer);
                _seenReceiveZeroWithEOF = true;
                if (_seenReceiveZeroWithAvailable)
                {
                    _bufferStyle = BufferStyle.UseZeroLengthBuffer;
                }
                // we'll let the calling method shut everything down
                return _zeroLengthBuffer;
            }

            // otherwise, we got something that looked like an EOF from receive,
            // but which wasn't really; we'll have to do things the hard way :(
            _bufferStyle = BufferStyle.UseSmallBuffer;
            return buffer;
        }

        private async void ReadFromChannelAndWriteToSocketAsync()
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
                        if (buffer.IsEmpty && _output.Reading.IsCompleted)
                        {
                            break;
                        }

                        foreach (var memory in buffer)
                        {
                            int remaining = memory.Length;
                            while (remaining != 0)
                            {
                                SetBuffer(memory, args, memory.Length - remaining);

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

                                remaining -= args.BytesTransferred;
                            }
                        }
                    }
                    finally
                    {
                        _output.Advance(buffer.End);
                    }
                }
                _output.CompleteReader();
            }
            catch (Exception ex)
            {
                // don't trust signal after an error; someone else could
                // still have it and invoke Set
                if (args != null)
                {
                    args.UserToken = null;
                }
                _output?.CompleteReader(ex);
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
        private unsafe void SetBuffer(Memory<byte> memory, SocketAsyncEventArgs args, int ignore = 0)
        {
            ArraySegment<byte> segment;
            if (!memory.TryGetArray(out segment))
            {
                throw new InvalidOperationException("Memory is not backed by an array; oops!");
            }
            args.SetBuffer(segment.Array, segment.Offset + ignore, segment.Count - ignore);
        }

        private void Shutdown()
        {
            Socket?.Shutdown(SocketShutdown.Both);
        }
    }
}