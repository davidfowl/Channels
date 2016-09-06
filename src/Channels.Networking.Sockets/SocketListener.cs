using System;
using System.Net;
using System.Net.Sockets;

namespace Channels.Networking.Sockets
{
    public class SocketListener : IDisposable
    {
        private readonly bool _ownsChannelFactory;
        private Socket _socket;
        private Socket Socket => _socket;
        private ChannelFactory _channelFactory;
        private ChannelFactory ChannelFactory => _channelFactory;
        private Action<SocketConnection> Callback { get; set; }
        static readonly EventHandler<SocketAsyncEventArgs> _asyncCompleted = OnAsyncCompleted;

        public SocketListener(ChannelFactory channelFactory = null)
        {
            _ownsChannelFactory = channelFactory == null;
            _channelFactory = channelFactory ?? new ChannelFactory();
        }

        public void Dispose() => Dispose(true);

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

        public void Start(IPEndPoint endpoint)
        {
            if (_socket == null)
            {
                _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                _socket.Bind(endpoint);
                _socket.Listen(10);
                var args = new SocketAsyncEventArgs(); // note: we keep re-using same args over for successive accepts
                                                       // so usefulness of pooling here minimal; this is per listener
                args.Completed += _asyncCompleted;
                args.UserToken = this;
                BeginAccept(args);
            }
        }

        public void Stop()
        {
            if (_socket != null)
            {
                try
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                catch { /* nom nom */ }
                _socket.Dispose();
                _socket = null;
            }
        }

        private Socket GetReusableSocket() => null; // TODO: socket pooling / re-use

        private void BeginAccept(SocketAsyncEventArgs args)
        {
            // keep trying to take sync; break when it goes async
            args.AcceptSocket = GetReusableSocket();
            while (!Socket.AcceptAsync(args))
            {
                OnAccept(args);
                args.AcceptSocket = GetReusableSocket();
            }
        }

        public void OnConnection(Action<SocketConnection> callback)
        {
            Callback = callback;
        }

        private static void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Accept:
                        var obj = (SocketListener)e.UserToken;
                        obj.OnAccept(e);
                        obj.BeginAccept(e);
                        break;
                }
            }
            catch { }
        }

        private void OnAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                var conn = new SocketConnection(e.AcceptSocket, ChannelFactory);
                e.AcceptSocket = null;
                Callback?.Invoke(conn);
            }

            // note that we don't want to call BeginAccept at the end of OnAccept, as that
            // will cause a stack-dive in the sync (backlog) case
        }
    }
}
