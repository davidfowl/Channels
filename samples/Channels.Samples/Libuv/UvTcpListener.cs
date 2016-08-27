using System;
using System.Net;
using System.Threading;
using Channels.Samples.Libuv.Interop;

namespace Channels.Samples.Libuv
{
    public class UvTcpListener
    {
        private static Action<UvStreamHandle, int, Exception, object> _onConnectionCallback = OnConnectionCallback;
        private static Action<Action<IntPtr>, IntPtr> _queueCloseCallback = QueueCloseHandle;

        private readonly Thread _thread = new Thread(OnStart);
        private readonly IPAddress _ip;
        private readonly int _port;

        private UvAsyncHandle _shutdownPostHandle;
        private UvTcpHandle _listenSocket;
        private Action<UvTcpServerConnection> _callback;

        public Uv Uv { get; private set; }

        public UvLoopHandle Loop { get; private set; }

        public ChannelFactory ChannelFactory { get; private set; } = new ChannelFactory();

        public UvTcpListener(IPAddress ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        public void OnConnection(Action<UvTcpServerConnection> callback)
        {
            _callback = callback;
        }

        private static void OnStart(object state)
        {
            ((UvTcpListener)state).RunLoop();
        }

        private void RunLoop()
        {
            Uv = new Uv();

            Loop = new UvLoopHandle();
            Loop.Init(Uv);

            _shutdownPostHandle = new UvAsyncHandle();
            _shutdownPostHandle.Init(Loop, OnPost, _queueCloseCallback);

            _listenSocket = new UvTcpHandle();
            _listenSocket.Init(Loop, _queueCloseCallback);
            _listenSocket.NoDelay(true);
            _listenSocket.Bind(new IPEndPoint(_ip, _port));

            _listenSocket.Listen(10, _onConnectionCallback, this);

            Uv.run(Loop, 0);
        }

        private void OnPost()
        {
            _listenSocket.Dispose();

            // Unreference the post handle
            _shutdownPostHandle.Unreference();
        }

        public void Start()
        {
            _thread.Start(this);
        }

        public void Stop()
        {
            _shutdownPostHandle.Send();

            _thread.Join();
        }

        private static void OnConnectionCallback(UvStreamHandle listenSocket, int status, Exception error, object state)
        {
            var listener = (UvTcpListener)state;

            var acceptSocket = new UvTcpHandle();

            try
            {
                acceptSocket.Init(listener.Loop, _queueCloseCallback);
                acceptSocket.NoDelay(true);
                listenSocket.Accept(acceptSocket);
                var connection = new UvTcpServerConnection(listener.ChannelFactory, listener.Loop, acceptSocket);
                listener._callback?.Invoke(connection);
            }
            catch (UvException)
            {
                acceptSocket.Dispose();
            }
        }

        private static void QueueCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
        }
    }
}