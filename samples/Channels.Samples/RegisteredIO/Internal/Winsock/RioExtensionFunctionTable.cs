// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Channels.Samples.Internal.Winsock
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RioExtensionFunctionTable
    {
        public UInt32 Size;

        public IntPtr RIOReceive;
        public IntPtr RIOReceiveEx;
        public IntPtr RIOSend;
        public IntPtr RIOSendEx;
        public IntPtr RIOCloseCompletionQueue;
        public IntPtr RIOCreateCompletionQueue;
        public IntPtr RIOCreateRequestQueue;
        public IntPtr RIODequeueCompletion;
        public IntPtr RIODeregisterBuffer;
        public IntPtr RIONotify;
        public IntPtr RIORegisterBuffer;
        public IntPtr RIOResizeCompletionQueue;
        public IntPtr RIOResizeRequestQueue;
    }
}