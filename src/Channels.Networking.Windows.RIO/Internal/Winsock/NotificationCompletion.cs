// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Channels.Networking.Windows.RIO.Internal.Winsock
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NotificationCompletion
    {
        public NotificationCompletionType Type;
        public NotificationCompletionIocp Iocp;
    }
}
