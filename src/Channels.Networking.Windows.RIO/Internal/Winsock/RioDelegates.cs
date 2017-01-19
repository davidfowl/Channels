﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Channels.Networking.Windows.RIO.Internal.Winsock
{
    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate IntPtr RioRegisterBuffer([In] IntPtr dataBuffer, [In] UInt32 dataLength);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate void RioDeregisterBuffer([In] IntPtr bufferId);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioSend([In] IntPtr socketQueue, [In] ref RioBufferSegment rioBuffer, [In] UInt32 dataBufferCount, [In] RioSendFlags flags, [In] long requestCorrelation);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public unsafe delegate bool RioSendCommit([In] IntPtr socketQueue, [In] RioBufferSegment* rioBuffer, [In] UInt32 dataBufferCount, [In] RioSendFlags flags, [In] long requestCorrelation);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioReceive([In] IntPtr socketQueue, [In] ref RioBufferSegment rioBuffer, [In] UInt32 dataBufferCount, [In] RioReceiveFlags flags, [In] long requestCorrelation);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate IntPtr RioCreateCompletionQueue([In] uint queueSize, [In] NotificationCompletion notificationCompletion);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate void RioCloseCompletionQueue([In] IntPtr cq);

    [SuppressUnmanagedCodeSecurity]
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

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate uint RioDequeueCompletion([In] IntPtr cq, [In] IntPtr resultArray, [In] uint resultArrayLength);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate Int32 RioNotify([In] IntPtr cq);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioResizeCompletionQueue([In] IntPtr cq, [In] UInt32 queueSize);

    [SuppressUnmanagedCodeSecurity]
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
    public delegate bool RioResizeRequestQueue([In] IntPtr rq, [In] UInt32 maxOutstandingReceive, [In] UInt32 maxOutstandingSend);

}
