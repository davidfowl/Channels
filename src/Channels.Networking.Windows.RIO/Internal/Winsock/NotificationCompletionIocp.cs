// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Channels.Networking.Windows.RIO.Internal.Winsock
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct NotificationCompletionIocp
    {
        public IntPtr IocpHandle;
        public ulong QueueCorrelation;
        public NativeOverlapped* Overlapped;
    }
}
