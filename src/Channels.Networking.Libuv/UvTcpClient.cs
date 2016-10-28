﻿using System;
using System.Net;
using System.Threading.Tasks;
using Channels.Networking.Libuv.Interop;

namespace Channels.Networking.Libuv
{
    public class UvTcpClient
    {
        private static readonly Action<UvConnectRequest, int, Exception, object> _connectCallback = OnConnection;
        private static readonly Action<object> _startConnect = state => ((UvTcpClient)state).DoConnect();

        private readonly TaskCompletionSource<UvTcpConnection> _connectTcs = new TaskCompletionSource<UvTcpConnection>();
        private readonly IPEndPoint _ipEndPoint;
        private readonly UvThread _thread;

        private UvTcpHandle _connectSocket;

        public UvTcpClient(UvThread thread, IPEndPoint endPoint)
        {
            _thread = thread;
            _ipEndPoint = endPoint;
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        public async Task<IChannel> ConnectAsync()
        {
            _thread.Post(_startConnect, this);

            var connection = await _connectTcs.Task;

            // Get back onto the current context
            await Task.Yield();

            return connection;
        }

        private void DoConnect()
        {
            _connectSocket = new UvTcpHandle();
            _connectSocket.Init(_thread.Loop, null);

            var connectReq = new UvConnectRequest();
            connectReq.Init(_thread.Loop);
            connectReq.Connect(_connectSocket, _ipEndPoint, _connectCallback, this);
        }

        private static void OnConnection(UvConnectRequest req, int status, Exception exception, object state)
        {
            var client = (UvTcpClient)state;

            var connection = new UvTcpConnection(client._thread, client._connectSocket);

            client._connectTcs.TrySetResult(connection);
        }
    }
}
