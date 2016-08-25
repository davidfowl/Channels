using System;
using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Channels.Samples
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
        private Action<UvTcpConnection> _callback;

        public Libuv Uv { get; private set; }

        public KestrelTrace Log { get; private set; }

        public UvLoopHandle Loop { get; private set; }

        public ChannelFactory ChannelFactory { get; private set; } = new ChannelFactory();

        public UvTcpListener(IPAddress ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        public void OnConnection(Action<UvTcpConnection> callback)
        {
            _callback = callback;
        }

        private static void OnStart(object state)
        {
            ((UvTcpListener)state).RunLoop();
        }

        private void RunLoop()
        {
            Uv = new Libuv();
            Log = new KestrelTrace(new LoggerFactory().CreateLogger<UvTcpListener>());

            Loop = new UvLoopHandle(Log);
            Loop.Init(Uv);

            _shutdownPostHandle = new UvAsyncHandle(Log);
            _shutdownPostHandle.Init(Loop, OnPost, _queueCloseCallback);

            _listenSocket = new UvTcpHandle(Log);
            _listenSocket.Init(Loop, _queueCloseCallback);
            _listenSocket.NoDelay(true);

            string host = null;
            if (_ip == IPAddress.Any)
            {
                host = "*";
            }
            else if (_ip == IPAddress.Loopback)
            {
                host = "localhost";
            }
            else
            {
                host = _ip.ToString();
            }

            var url = $"http://{host}:{_port}";
            var address = Microsoft.AspNetCore.Server.Kestrel.ServerAddress.FromUrl(url);
            _listenSocket.Bind(address);

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

            var acceptSocket = new UvTcpHandle(listener.Log);

            try
            {
                acceptSocket.Init(listener.Loop, _queueCloseCallback);
                acceptSocket.NoDelay(true);
                listenSocket.Accept(acceptSocket);
                var connection = new UvTcpConnection(listener, acceptSocket);
                listener._callback?.Invoke(connection);
            }
            catch (UvException ex)
            {
                listener.Log.LogError(0, ex, "UvTcpListener.OnConnection");
                acceptSocket.Dispose();
            }
        }

        private static void QueueCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
        }
    }
}