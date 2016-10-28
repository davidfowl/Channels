using System;
using System.Net;
using System.Threading.Tasks;
using Channels.Networking.Libuv.Interop;

namespace Channels.Networking.Libuv
{
    public class UvTcpListener : IChannelEndPoint , ICallbackOnConnection
    {
        private static Action<UvStreamHandle, int, Exception, object> _onConnectionCallback = OnConnectionCallback;
        private static Action<object> _startListeningCallback = state => ((UvTcpListener)state).Listen();
        private static Action<object> _stopListeningCallback = state => ((UvTcpListener)state).Dispose();

        private readonly IPEndPoint _endpoint;
        private readonly UvThread _thread;

        private UvTcpHandle _listenSocket;
        private Func<UvTcpConnection, Task> _callback;

        /// <summary>
        /// </summary>
        /// <param name="thread"></param>
        /// <param name="endpoint"></param>
        public UvTcpListener(UvThread thread, IPEndPoint endpoint)
        {
            _thread = thread;
            _endpoint = endpoint;
        }

        /// <summary>
        /// </summary>
        public IPEndPoint EndPoint      
        {
            get; private set;
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

        /// <summary>
        /// callback function when a connection happens, IChannel
        /// is used so the callback need not know about the connection
        /// concrete implementation.
        /// </summary>
        /// <param name="callback"></param>
        public void OnConnection(Func<IChannel, Task> callback)
        {
            _callback = callback;
        }




        private void Dispose()
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
                ExecuteCallback(listener, connection);
            }
            catch (UvException)
            {
                acceptSocket.Dispose();
            }
        }

        private static async void ExecuteCallback(UvTcpListener listener, UvTcpConnection connection)
        {
            try
            {
                await listener._callback?.Invoke(connection);
            }
            catch
            {
                // Swallow exceptions
            }
            finally
            {
                // Dispose the connection on task completion
                connection.Dispose();
            }
        }
    }
}