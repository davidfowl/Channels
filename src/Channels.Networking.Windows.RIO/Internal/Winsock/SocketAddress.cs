// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Channels.Networking.Windows.RIO.Internal.Winsock
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SocketAddress
    {
        public AddressFamilies Family;
        public ushort Port;
        public Ipv4InternetAddress IpAddress;
        public fixed byte Padding[8];
    }
}