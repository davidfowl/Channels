// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Channels.Samples.Internal;
using Channels.Samples.Internal.Winsock;

namespace Channels.Samples
{
    public sealed class RioTcpServer
    {
        private readonly IntPtr _socket;
        private readonly RegisteredIO _rio;
        private readonly RioThreadPool _pool;

        private long _connectionId;

        public RioTcpServer(ushort port, byte address1, byte address2, byte address3, byte address4)
        {
            var version = new Version(2, 2);
            WindowsSocketsData wsaData;
            System.Net.Sockets.SocketError result = RioImports.WSAStartup((short)version.Raw, out wsaData);
            if (result != System.Net.Sockets.SocketError.Success)
            {
                var error = RioImports.WSAGetLastError();
                throw new Exception(string.Format("ERROR: WSAStartup returned {0}", error));
            }

            _socket = RioImports.WSASocket(AddressFamilies.Internet, SocketType.Stream, Protocol.IpProtocolTcp, IntPtr.Zero, 0, SocketFlags.RegisteredIO);
            if (_socket == IntPtr.Zero)
            {
                var error = RioImports.WSAGetLastError();
                RioImports.WSACleanup();
                throw new Exception(string.Format("ERROR: WSASocket returned {0}", error));
            }

            _rio = RioImports.Initalize(_socket);


            _pool = new RioThreadPool(_rio, _socket, CancellationToken.None);
            _connectionId = 0;
            Start(port, address1, address2, address3, address4);
        }

        private void Start(ushort port, byte address1, byte address2, byte address3, byte address4)
        {
            // BIND
            var inAddress = new Ipv4InternetAddress();
            inAddress.Byte1 = address1;
            inAddress.Byte2 = address2;
            inAddress.Byte3 = address3;
            inAddress.Byte4 = address4;

            var sa = new SocketAddress();
            sa.Family = AddressFamilies.Internet;
            sa.Port = RioImports.htons(port);
            sa.IpAddress = inAddress;

            int result;
            unsafe
            {
                var size = sizeof(SocketAddress);
                result = RioImports.bind(_socket, ref sa, size);
            }
            if (result == RioImports.SocketError)
            {
                RioImports.WSACleanup();
                throw new Exception("bind failed");
            }

            // LISTEN
            result = RioImports.listen(_socket, 2048);
            if (result == RioImports.SocketError)
            {
                RioImports.WSACleanup();
                throw new Exception("listen failed");
            }
        }
        public RioTcpConnection Accept()
        {
            var accepted = RioImports.accept(_socket, IntPtr.Zero, 0);
            if (accepted == new IntPtr(-1))
            {
                var error = RioImports.WSAGetLastError();
                RioImports.WSACleanup();
                throw new Exception(string.Format("listen failed with {0}", error));
            }
            var connectionId = Interlocked.Increment(ref _connectionId);
            var thread = _pool.GetThread(connectionId);

            var requestQueue = _rio.RioCreateRequestQueue(accepted, RioTcpConnection.MaxPendingReceives + RioTcpConnection.IOCPOverflowEvents, 1, RioTcpConnection.MaxPendingSends + RioTcpConnection.IOCPOverflowEvents, 1, thread.CompletionQueue, thread.CompletionQueue, connectionId);
            if (requestQueue == IntPtr.Zero)
            {
                var error = RioImports.WSAGetLastError();
                RioImports.WSACleanup();
                throw new Exception(String.Format("ERROR: RioCreateRequestQueue returned {0}", error));
            }

            return new RioTcpConnection(accepted, connectionId, requestQueue, thread, _rio);
        }

        public void Stop()
        {
            RioImports.WSACleanup();
        }

        public struct Version
        {
            public ushort Raw;

            public Version(byte major, byte minor)
            {
                Raw = major;
                Raw <<= 8;
                Raw += minor;
            }

            public byte Major
            {
                get
                {
                    ushort result = Raw;
                    result >>= 8;
                    return (byte)result;
                }
            }

            public byte Minor
            {
                get
                {
                    ushort result = Raw;
                    result &= 0x00FF;
                    return (byte)result;
                }
            }

            public override string ToString()
            {
                return string.Format("{0}.{1}", Major, Minor);
            }
        }
    }
    
}
