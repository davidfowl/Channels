// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;

namespace Channels
{
    public struct WritableBuffer
    {
        private LinkedSegment _segment;
        // TODO: remove _block, use the one in _segment
        private int _index;

        internal WritableBuffer(LinkedSegment segment)
        {
            _segment = segment;
            _index = segment?.Start ?? 0;
        }

        internal WritableBuffer(LinkedSegment segment, int index)
        {
            _segment = segment;
            _index = index;
        }

        internal LinkedSegment Segment => _segment;

        internal int Index => _index;

        internal bool IsDefault => _segment == null;

        public BufferSpan Memory => new BufferSpan(_segment.Block.DataArrayPtr, _segment.Block.Array, Segment.End, _segment.Block.Data.Offset + _segment.Block.Data.Count - Segment.End);

        public void Write(byte[] data, int offset, int count)
        {
            if (IsDefault)
            {
                return;
            }

            Debug.Assert(_segment.Block != null);
            Debug.Assert(_segment.Next == null);
            Debug.Assert(_segment.End == _index);

            var block = _segment.Block;
            var pool = block.Pool;
            var segment = _segment;
            var blockIndex = _index;

            var bufferIndex = offset;
            var remaining = count;
            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            while (remaining > 0)
            {
                // Try the block empty if the segment is reaodnly
                if (bytesLeftInBlock == 0 || segment.ReadOnly)
                {
                    var nextBlock = pool.Lease();
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
            _segment = segment;
            _index = blockIndex;
        }

        public void Append(ReadableBuffer begin, ReadableBuffer end)
        {
            Debug.Assert(_segment != null);
            Debug.Assert(_segment.Block != null);
            Debug.Assert(_segment.Next == null);
            Debug.Assert(_segment.End == _index);

            var clonedBegin = LinkedSegment.Clone(begin, end);
            var clonedEnd = clonedBegin;
            while (clonedEnd.Next != null)
            {
                clonedEnd = clonedEnd.Next;
            }

            _segment.Next = clonedBegin;
            _segment = clonedEnd;
            _index = clonedEnd.End;
        }

        public void UpdateWritten(int bytesWritten)
        {
            Debug.Assert(_segment != null);
            Debug.Assert(!_segment.ReadOnly);
            Debug.Assert(_segment.Block != null);
            Debug.Assert(_segment.Next == null);
            Debug.Assert(_segment.End == _index);

            var block = _segment.Block;
            var blockIndex = _index + bytesWritten;

            Debug.Assert(blockIndex <= block.Data.Offset + block.Data.Count);

            _segment.End = blockIndex;
            _index = blockIndex;
        }
    }
}
