// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;

namespace Channels
{
    public struct WritableBuffer
    {
        private MemoryPool _pool;

        private LinkedSegment _tail;
        private int _tailIndex;

        private LinkedSegment _head;
        private int _headIndex;

        internal WritableBuffer(MemoryPool pool, LinkedSegment segment)
        {
            _pool = pool;

            _tail = segment;
            _tailIndex = segment?.End ?? 0;

            _head = segment;
            _headIndex = _tailIndex;
        }

        internal LinkedSegment Head => _head;

        internal int HeadIndex => _headIndex;

        internal LinkedSegment Tail => _tail;

        internal int TailIndex => _tailIndex;

        internal bool IsDefault => _tail == null;

        public BufferSpan Memory => new BufferSpan(_tail.Block.DataArrayPtr, _tail.Block.Array, Tail.End, _tail.Block.Data.Offset + _tail.Block.Data.Count - Tail.End);

        public void Write(byte[] data, int offset, int count)
        {
            if (_tail == null)
            {
                _tail = new LinkedSegment(_pool.Lease());
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
                    var nextSegment = new LinkedSegment(nextBlock);
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

        public void Append(ReadableBuffer begin, ReadableBuffer end)
        {
            var clonedBegin = LinkedSegment.Clone(begin, end);
            var clonedEnd = clonedBegin;
            while (clonedEnd.Next != null)
            {
                clonedEnd = clonedEnd.Next;
            }

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

        public void UpdateWritten(int bytesWritten)
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
