// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Channels.Networking.Windows.RIO.Internal.Winsock
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsSocketsData
    {
        internal short Version;
        internal short HighVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        internal string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
        internal string SystemStatus;
        internal short MaxSockets;
        internal short MaxDatagramSize;
        internal IntPtr VendorInfo;
    }
}