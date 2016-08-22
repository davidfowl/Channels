// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Channels.Samples.Internal.Winsock
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate IntPtr RioRegisterBuffer([In] IntPtr dataBuffer, [In] UInt32 dataLength);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate void RioDeregisterBuffer([In] IntPtr bufferId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public unsafe delegate bool RioSend([In] IntPtr socketQueue, [In] BufferSegment* rioBuffer, [In] UInt32 dataBufferCount, [In] RioSendFlags flags, [In] long requestCorrelation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioReceive([In] IntPtr socketQueue, [In] ref BufferSegment rioBuffer, [In] UInt32 dataBufferCount, [In] RioReceiveFlags flags, [In] long requestCorrelation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate IntPtr RioCreateCompletionQueue([In] uint queueSize, [In] NotificationCompletion notificationCompletion);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate void RioCloseCompletionQueue([In] IntPtr cq);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate IntPtr RioCreateRequestQueue(
                                    [In] IntPtr socket,
                                    [In] UInt32 maxOutstandingReceive,
                                    [In] UInt32 maxReceiveDataBuffers,
                                    [In] UInt32 maxOutstandingSend,
                                    [In] UInt32 maxSendDataBuffers,
                                    [In] IntPtr receiveCq,
                                    [In] IntPtr sendCq,
                                    [In] long connectionCorrelation
                                );

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate uint RioDequeueCompletion([In] IntPtr cq, [In] IntPtr resultArray, [In] uint resultArrayLength);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate Int32 RioNotify([In] IntPtr cq);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioResizeCompletionQueue([In] IntPtr cq, [In] UInt32 queueSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioResizeRequestQueue([In] IntPtr rq, [In] UInt32 maxOutstandingReceive, [In] UInt32 maxOutstandingSend);

}
