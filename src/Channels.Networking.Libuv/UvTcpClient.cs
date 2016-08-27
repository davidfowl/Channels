using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Channels.Networking.Libuv.Interop;

namespace Channels.Networking.Libuv
{
    public class UvTcpClient
    {
        private static Action<Action<IntPtr>, IntPtr> _queueCloseCallback = QueueCloseHandle;

        private readonly Thread _thread = new Thread(OnStart);
        private readonly IPAddress _ip;
        private readonly int _port;

        private UvAsyncHandle _shutdownPostHandle;
        private UvTcpHandle _connectSocket;
        private TaskCompletionSource<object> _connectTcs = new TaskCompletionSource<object>();

        public Uv Uv { get; private set; }

        public UvLoopHandle Loop { get; private set; }

        public ChannelFactory ChannelFactory { get; private set; } = new ChannelFactory();

        public UvTcpClient(IPAddress ip, int port)
        {
            _ip = ip;
            _port = port;
        }

        public async Task<UvTcpClientConnection> ConnectAsync()
        {
            if (!_thread.IsAlive)
            {
                Start();
            }

            await _connectTcs.Task;
            return new UvTcpClientConnection(ChannelFactory, Loop, _connectSocket);
        }

        private static void OnStart(object state)
        {
            ((UvTcpClient)state).RunLoop();
        }

        private void RunLoop()
        {
            Uv = new Uv();

            Loop = new UvLoopHandle();
            Loop.Init(Uv);

            _shutdownPostHandle = new UvAsyncHandle();
            _shutdownPostHandle.Init(Loop, OnPost, _queueCloseCallback);

            _connectSocket = new UvTcpHandle();
            _connectSocket.Init(Loop, _queueCloseCallback);
            _connectSocket.NoDelay(true);

            var connectReq = new UvConnectRequest();
            connectReq.Init(Loop);
            connectReq.Connect(_connectSocket, new IPEndPoint(_ip, _port), OnConnection , this);

            Uv.run(Loop, 0);
        }

        private static void OnConnection(UvConnectRequest req, int status, Exception exception, object state)
        {
            var client = (UvTcpClient)state;

            client._connectTcs.TrySetResult(null);
        }

        private void OnPost()
        {
            _connectSocket.Dispose();

            // Unreference the post handle
            _shutdownPostHandle.Unreference();
        }

        private void Start()
        {
            _thread.Start(this);
        }

        public void Disconnect()
        {
            _shutdownPostHandle.Send();

            _thread.Join();
        }

        private static void QueueCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
        }
    }
}
