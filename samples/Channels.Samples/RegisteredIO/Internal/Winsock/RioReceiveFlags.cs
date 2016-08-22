// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels.Samples.Internal.Winsock
{
    public enum RioReceiveFlags : uint
    {
        None = 0x00000000,
        DontNotify = 0x00000001,
        Defer = 0x00000002,
        Waitall = 0x00000004,
        CommitOnly = 0x00000008
    }
}