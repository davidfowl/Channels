// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Channels
{
    internal interface IBufferSegmentFactory
    {
        BufferSegment Create(IBuffer buffer);

        BufferSegment Create(IBuffer buffer, int start, int end);

        void Dispose(BufferSegment segment);
    }
}