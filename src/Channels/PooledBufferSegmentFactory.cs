// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Channels
{
    internal class PooledBufferSegmentFactory : IBufferSegmentFactory
    {
        private readonly ObjectPool<PooledBufferSegment> _segmentPool;

        public PooledBufferSegmentFactory()
        {
            _segmentPool = new ObjectPool<PooledBufferSegment>(() => new PooledBufferSegment(), Environment.ProcessorCount * 16);
        }

        public BufferSegment Create(IBuffer buffer)
        {
            var segment = _segmentPool.Allocate();
            segment.Initialize(buffer);
            return segment;
        }

        public BufferSegment Create(IBuffer buffer, int start, int end)
        {
            var segment = _segmentPool.Allocate();
            segment.Initialize(buffer, start, end);
            return segment;
        }

        public void Dispose(BufferSegment segment)
        {
            segment.Dispose();
            _segmentPool.Free((PooledBufferSegment)segment);
        }
    }
}