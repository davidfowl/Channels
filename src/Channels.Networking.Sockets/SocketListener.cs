using System;
using System.Net;
using System.Net.Sockets;

namespace Channels.Networking.Sockets
{
    public class SocketListener : IDisposable
    {
        private bool ownsChannelFactory;
        private Socket listenSocket;
        private ChannelFactory channelFactory;

        public SocketListener(ChannelFactory channelFactory = null)
        {
            this.ownsChannelFactory = channelFactory != null;
            this.channelFactory = channelFactory ?? new ChannelFactory();
        }
        private Action<SocketConnection> _callback;

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                GC.SuppressFinalize(this);
                listenSocket?.Dispose();
                listenSocket = null;
                if (ownsChannelFactory) channelFactory?.Dispose();
                channelFactory = null;
            }
        }

        
        public void Start(IPEndPoint endpoint)
        {
            if(listenSocket == null)
            {
                listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                listenSocket.Bind(endpoint);
                listenSocket.Listen(10);
                var args = new SocketAsyncEventArgs(); // keep re-using same args
                args.Completed += AsyncCompleted;
                args.UserToken = this;
                BeginAccept(args);
            }
        }
        public void Stop()
        {
            if(listenSocket != null)
            {
                try { listenSocket.Shutdown(SocketShutdown.Both); } catch { }
                listenSocket.Dispose();
                listenSocket = null;
            }
        }

        private Socket GetReusableSocket() => null; // TODO: socket pooling / re-use
        private void BeginAccept(SocketAsyncEventArgs args)
        {
            // keep trying to take sync; break when it goes async
            args.AcceptSocket = GetReusableSocket();
            while(!listenSocket.AcceptAsync(args))
            {
                OnAccept(args);
                args.AcceptSocket = GetReusableSocket();
            }
        }

        public void OnConnection(Action<SocketConnection> callback)
        {
            _callback = callback;
        }

        static readonly EventHandler<SocketAsyncEventArgs> AsyncCompleted = (sender, args)
             => OnAsyncCompleted(sender, args);
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
            var conn = new SocketConnection(e.AcceptSocket, channelFactory);
            e.AcceptSocket = null;
            _callback?.Invoke(conn);

            // note that we don't want to call BeginAccept at the end of OnAccept, as that
            // will cause a stack-dive in the sync (backlog) case
        }
    }
}
