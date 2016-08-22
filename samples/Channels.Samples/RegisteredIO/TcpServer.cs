// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using IllyriadGames.River.Internal;
using IllyriadGames.River.Internal.Winsock;

namespace IllyriadGames.River
{
    public sealed class TcpServer
    {
        IntPtr _socket;
        Internal.Winsock.RegisteredIO _rio;
        Internal.ThreadPool _pool;

        long _connectionId;

        public TcpServer(ushort port, byte address1, byte address2, byte address3, byte address4)
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


            _pool = new Internal.ThreadPool(_rio, _socket, CancellationToken.None);
            _connectionId = 0;
            Start(port, address1, address2, address3, address4);
        }

        private void Start(ushort port, byte address1, byte address2, byte address3, byte address4)
        {
            // BIND
            Ipv4InternetAddress inAddress = new Ipv4InternetAddress();
            inAddress.Byte1 = address1;
            inAddress.Byte2 = address2;
            inAddress.Byte3 = address3;
            inAddress.Byte4 = address4;

            SocketAddress sa = new SocketAddress();
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
        public TcpConnection Accept()
        {
            IntPtr accepted = RioImports.accept(_socket, IntPtr.Zero, 0);
            if (accepted == new IntPtr(-1))
            {
                var error = RioImports.WSAGetLastError();
                RioImports.WSACleanup();
                throw new Exception(string.Format("listen failed with {0}", error));
            }
            var connection = Interlocked.Increment(ref _connectionId);
            return new TcpConnection(accepted, connection, _pool.GetThread(connection), _rio);
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
