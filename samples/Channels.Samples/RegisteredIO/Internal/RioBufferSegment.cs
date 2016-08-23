// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Channels.Samples.Internal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RioBufferSegment
    {
        public RioBufferSegment(IntPtr bufferId, uint offset, uint length)
        {
            BufferId = bufferId;
            Offset = offset;
            Length = length;
        }

        IntPtr BufferId;
        public readonly uint Offset;
        public uint Length;
    }
}
