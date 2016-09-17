// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels
{
    internal class PooledBufferSegment : BufferSegment
    {
        public new void Initialize(IBuffer buffer)
        {
            base.Initialize(buffer);
        }

        public new void Initialize(IBuffer buffer, int start, int end)
        {
            base.Initialize(buffer, start, end);
        }
    }
}