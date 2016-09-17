// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels
{
    internal class TransientBufferSegmentFactory: IBufferSegmentFactory
    {
        public static IBufferSegmentFactory Default { get; } = new PooledBufferSegmentFactory();

        public BufferSegment Create(IBuffer buffer)
        {
            return new TransientBufferSegment(buffer);
        }

        public BufferSegment Create(IBuffer buffer, int start, int end)
        {
            return new TransientBufferSegment(buffer, start, end);
        }

        public void Dispose(BufferSegment segment)
        {
            segment.Dispose();
        }
    }
}