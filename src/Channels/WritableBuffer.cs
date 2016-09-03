// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public struct WritableBuffer
    {
        private MemoryPool _pool;
        private Channel _channel;

        private MemoryBlockSegment _tail;
        private int _tailIndex;

        private MemoryBlockSegment _head;
        private int _headIndex;

        private bool _comitted;

        internal WritableBuffer(Channel channel, MemoryPool pool, MemoryBlockSegment segment)
        {
            _channel = channel;
            _pool = pool;

            _tail = segment;
            _tailIndex = segment?.End ?? 0;

            _head = segment;
            _headIndex = _tailIndex;

            _comitted = false;
        }

        internal MemoryBlockSegment Head => _head;

        internal int HeadIndex => _headIndex;

        internal MemoryBlockSegment Tail => _tail;

        internal int TailIndex => _tailIndex;

        internal bool IsDefault => _tail == null;

        public BufferSpan Memory => new BufferSpan(_tail, _tail.End, _tail.Block.Data.Offset + _tail.Block.Data.Count - _tail.End);

        public void Ensure(int count)
        {
            if (_tail == null)
            {
                _tail = new MemoryBlockSegment(_pool.Lease());
                _tailIndex = _tail.End;
                _head = _tail;
                _headIndex = _tail.Start;
            }

            Debug.Assert(_tail.Block != null);
            Debug.Assert(_tail.Next == null);
            Debug.Assert(_tail.End == _tailIndex);

            var segment = _tail;
            var block = _tail.Block;
            var blockIndex = _tailIndex;
            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            // If inadequate bytes left or if the segment is readonly
            if (bytesLeftInBlock < count || segment.ReadOnly)
            {
                var nextBlock = _pool.Lease();
                var nextSegment = new MemoryBlockSegment(nextBlock);
                segment.End = blockIndex;
                Volatile.Write(ref segment.Next, nextSegment);
                segment = nextSegment;
                block = nextBlock;

                blockIndex = block.Data.Offset;

                segment.End = blockIndex;
                segment.Block = block;
                _tail = segment;
                _tailIndex = blockIndex;
            }
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (_tail == null)
            {
                _tail = new MemoryBlockSegment(_pool.Lease());
                _tailIndex = _tail.End;
                _head = _tail;
                _headIndex = _tail.Start;
            }

            Debug.Assert(_tail.Block != null);
            Debug.Assert(_tail.Next == null);
            Debug.Assert(_tail.End == _tailIndex);

            var segment = _tail;
            var block = _tail.Block;
            var blockIndex = _tailIndex;
            var bufferIndex = offset;
            var remaining = count;
            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            while (remaining > 0)
            {
                // Try the block empty if the segment is reaodnly
                if (bytesLeftInBlock == 0 || segment.ReadOnly)
                {
                    var nextBlock = _pool.Lease();
                    var nextSegment = new MemoryBlockSegment(nextBlock);
                    segment.End = blockIndex;
                    Volatile.Write(ref segment.Next, nextSegment);
                    segment = nextSegment;
                    block = nextBlock;

                    blockIndex = block.Data.Offset;
                    bytesLeftInBlock = block.Data.Count;
                }

                var bytesToCopy = remaining < bytesLeftInBlock ? remaining : bytesLeftInBlock;

                Buffer.BlockCopy(data, bufferIndex, block.Array, blockIndex, bytesToCopy);

                blockIndex += bytesToCopy;
                bufferIndex += bytesToCopy;
                remaining -= bytesToCopy;
                bytesLeftInBlock -= bytesToCopy;
            }

            segment.End = blockIndex;
            segment.Block = block;
            _tail = segment;
            _tailIndex = blockIndex;
        }

        public void Append(ref ReadableBuffer buffer)
        {
            MemoryBlockSegment clonedEnd;
            var clonedBegin = MemoryBlockSegment.Clone(buffer.Start, buffer.End, out clonedEnd);

            if (_tail == null)
            {
                _head = clonedBegin;
                _headIndex = clonedBegin.Start;
            }
            else
            {
                Debug.Assert(_tail.Block != null);
                Debug.Assert(_tail.Next == null);
                Debug.Assert(_tail.End == _tailIndex);

                _tail.Next = clonedBegin;
            }

            _tail = clonedEnd;
            _tailIndex = clonedEnd.End;
        }

        public void Commit()
        {
            if (!_comitted)
            {
                _channel.Append(this);

                _comitted = true;
            }
        }

        public Task FlushAsync()
        {
            Commit();
            return _channel.CompleteWriteAsync();
        }

        public void CommitBytes(int bytesWritten)
        {
            Debug.Assert(_tail != null);
            Debug.Assert(!_tail.ReadOnly);
            Debug.Assert(_tail.Block != null);
            Debug.Assert(_tail.Next == null);
            Debug.Assert(_tail.End == _tailIndex);

            var block = _tail.Block;
            var blockIndex = _tailIndex + bytesWritten;

            Debug.Assert(blockIndex <= block.Data.Offset + block.Data.Count);

            _tail.End = blockIndex;
            _tailIndex = blockIndex;
        }
    }
}
