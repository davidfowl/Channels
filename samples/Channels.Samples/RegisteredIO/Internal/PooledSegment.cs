// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace IllyriadGames.River.Internal
{
    public struct PooledSegment : IDisposable
    {
        public readonly byte[] Buffer;
        internal BufferSegment RioBuffer;
        public readonly int PoolIndex;
        private BufferPool _owningPool;
        internal PooledSegment(int index, BufferPool owningPool, BufferSegment segment, byte[] buffer)
        {
            PoolIndex = index;
            _owningPool = owningPool;
            RioBuffer = segment;
            Buffer = buffer;
        }

        public int Offset
        {
            get
            {
                return (int)RioBuffer.Offset;
            }
        }

        public void Dispose()
        {
            _owningPool.ReleaseBuffer(PoolIndex);
        }
    }
}
