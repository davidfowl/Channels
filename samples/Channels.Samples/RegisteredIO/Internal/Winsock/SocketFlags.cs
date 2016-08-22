// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels.Samples.Internal.Winsock
{
    public enum SocketFlags : uint
    {
        Overlapped = 0x01,
        MultipointCRoot = 0x02,
        MultipointCLeaf = 0x04,
        MultipointDRoot = 0x08,
        MultipointDLeaf = 0x10,
        AccessSystemSecurity = 0x40,
        NoHandleInherit = 0x80,
        RegisteredIO = 0x100
    }
}