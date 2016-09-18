using System;
using System.Net;
using Channels.Networking.Libuv.Interop;

namespace Channels.Networking.Libuv
{
    public class UvTcpListener : IDisposable
    {
        private static Action<UvStreamHandle, int, Exception, object> _onConnectionCallback = OnConnectionCallback;
        private static Action<object> _startListeningCallback = state => ((UvTcpListener)state).Listen();
        private static Action<object> _stopListeningCallback = state => ((UvTcpListener)state).Dispose();

        private readonly IPEndPoint _endpoint;
        private readonly UvThread _thread;

        private UvTcpHandle _listenSocket;
        private Action<UvTcpConnection> _callback;

        public UvTcpListener(UvThread thread, IPEndPoint endpoint)
        {
            _thread = thread;
            _endpoint = endpoint;
        }

        public void OnConnection(Action<UvTcpConnection> callback)
        {
            _callback = callback;
        }

        public void Start()
        {
            // TODO: Make idempotent
            _thread.Post(_startListeningCallback, this);
        }

        public void Stop()
        {
            // TODO: Make idempotent
            _thread.Post(_stopListeningCallback, this);
        }

        // review: should this also stop?
        public void Dispose()
        {
            _listenSocket.Dispose();
        }

        private void Listen()
        {
            _listenSocket = new UvTcpHandle();
            _listenSocket.Init(_thread.Loop, null);
            _listenSocket.NoDelay(true);
            _listenSocket.Bind(_endpoint);
            _listenSocket.Listen(10, _onConnectionCallback, this);
        }

        private static void OnConnectionCallback(UvStreamHandle listenSocket, int status, Exception error, object state)
        {
            var listener = (UvTcpListener)state;

            var acceptSocket = new UvTcpHandle();

            try
            {
                acceptSocket.Init(listener._thread.Loop, null);
                acceptSocket.NoDelay(true);
                listenSocket.Accept(acceptSocket);
                var connection = new UvTcpConnection(listener._thread, acceptSocket);
                listener._callback?.Invoke(connection);
            }
            catch (UvException)
            {
                acceptSocket.Dispose();
            }
        }
    }
}