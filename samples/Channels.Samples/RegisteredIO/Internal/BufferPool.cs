// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Channels.Samples.Internal
{
    public sealed class BufferPool : IDisposable
    {
        BufferSegment[] _segments;
        private byte[] _underlyingBuffer;
        public const int PacketSize = (1500 - (20 + 20)) * 4; // MTU - (IPv4 Header + TCP Header)
        private const int PooledPacketSize = PacketSize + 12 + 64; // PacketSize + 12 + 64 w false sharing cache guard bytes
        private const int PerAllocationCount = RioThreadPool.PreAllocSocketsPerThread * 2;
        private const int BufferLength = (PooledPacketSize) * PerAllocationCount; // Amount to pin per alloc 9.4 MB ish; into LOH

        private ConcurrentQueue<int> _availableSegments;
        private ConcurrentQueue<AllocatedBuffer> _allocatedBuffers;
        private Winsock.RegisteredIO _rio;

        private struct AllocatedBuffer
        {
            public byte[] Buffer;
            public GCHandle PinnedBuffer;
            public IntPtr BufferId;
        }

        public BufferPool(Winsock.RegisteredIO rio)
        {
            _rio = rio;
            _allocatedBuffers = new ConcurrentQueue<AllocatedBuffer>();
            _availableSegments = new ConcurrentQueue<int>();

            _underlyingBuffer = new byte[BufferLength];
            
        }

        public void Initalize()
        {

            var pinnedBuffer = GCHandle.Alloc(_underlyingBuffer, GCHandleType.Pinned);
            var address = Marshal.UnsafeAddrOfPinnedArrayElement(_underlyingBuffer, 0);
            var bufferId = _rio.RioRegisterBuffer(address, BufferLength);

            _allocatedBuffers.Enqueue(new AllocatedBuffer() { Buffer = _underlyingBuffer, PinnedBuffer = pinnedBuffer, BufferId = bufferId });

            _segments = new BufferSegment[PerAllocationCount];
            _availableSegments = new ConcurrentQueue<int>();
            var offset = 0u;
            for (var i = 0; i < _segments.Length; i++)
            {
                _segments[i] = new BufferSegment(bufferId, offset, PacketSize);
                _availableSegments.Enqueue(i);
                offset += PooledPacketSize;
            }

        }

        public PooledSegment GetBuffer()
        {
            int bufferNo;
            if (_availableSegments.TryDequeue(out bufferNo))
            {
                return new PooledSegment(bufferNo, this, _segments[bufferNo], _underlyingBuffer);
            }
            else
            {
                throw new NotImplementedException("Out of pooled buffers; not implemented dynamic expansion");
            }
        }
        internal void ReleaseBuffer(int bufferIndex)
        {
            _availableSegments.Enqueue(bufferIndex);
        }

        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                AllocatedBuffer buffer;
                while (_allocatedBuffers.TryDequeue(out buffer))
                {
                    _rio.DeregisterBuffer(buffer.BufferId);
                    buffer.PinnedBuffer.Free();
                }

                if (disposing)
                {
                    _segments = null;
                    _underlyingBuffer = null;
                    _rio = null;
                    _availableSegments = null;
                    _allocatedBuffers = null;
                }

                disposedValue = true;
            }
        }

        ~BufferPool()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

}
