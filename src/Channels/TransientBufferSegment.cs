// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels
{
    internal class TransientBufferSegment : BufferSegment
    {
        public TransientBufferSegment(IBuffer buffer)
        {
            Initialize(buffer);
        }

        public TransientBufferSegment(IBuffer buffer, int start, int end)
        {
            Initialize(buffer, start, end);
        }
    }
}