using System;
using System.Net;
using System.Net.Sockets;

namespace Channels.Networking.Sockets
{
    public class SocketListener : IDisposable
    {
        private bool _ownsChannelFactory;
        private Socket _listenSocket;
        private ChannelFactory _channelFactory;
        private Action<SocketConnection> _callback;
        static readonly EventHandler<SocketAsyncEventArgs> _asyncCompleted = OnAsyncCompleted;
        public SocketListener(ChannelFactory channelFactory = null)
        {
            _ownsChannelFactory = channelFactory != null;
            _channelFactory = channelFactory ?? new ChannelFactory();
        }

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                GC.SuppressFinalize(this);
                _listenSocket?.Dispose();
                _listenSocket = null;
                if (_ownsChannelFactory) { _channelFactory?.Dispose(); }
                _channelFactory = null;
            }
        }


        public void Start(IPEndPoint endpoint)
        {
            if (_listenSocket == null)
            {
                _listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                _listenSocket.Bind(endpoint);
                _listenSocket.Listen(10);
                var args = new SocketAsyncEventArgs(); // keep re-using same args
                args.Completed += _asyncCompleted;
                args.UserToken = this;
                BeginAccept(args);
            }
        }
        public void Stop()
        {
            if (_listenSocket != null)
            {
                try { _listenSocket.Shutdown(SocketShutdown.Both); } catch { }
                _listenSocket.Dispose();
                _listenSocket = null;
            }
        }

        private Socket GetReusableSocket() => null; // TODO: socket pooling / re-use
        private void BeginAccept(SocketAsyncEventArgs args)
        {
            // keep trying to take sync; break when it goes async
            args.AcceptSocket = GetReusableSocket();
            while (!_listenSocket.AcceptAsync(args))
            {
                OnAccept(args);
                args.AcceptSocket = GetReusableSocket();
            }
        }

        public void OnConnection(Action<SocketConnection> callback)
        {
            _callback = callback;
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
            var conn = new SocketConnection(e.AcceptSocket, _channelFactory);
            e.AcceptSocket = null;
            _callback?.Invoke(conn);

            // note that we don't want to call BeginAccept at the end of OnAccept, as that
            // will cause a stack-dive in the sync (backlog) case
        }
    }
}
