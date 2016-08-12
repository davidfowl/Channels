// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Numerics;
using System.Text;
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

        internal MemoryPoolBlock Block => ;

        internal int Index => _index;

        internal bool IsDefault => _block == null;

        public BufferSpan Memory => new BufferSpan(Block.DataArrayPtr, Block.Array, Segment.End, Block.Data.Offset + Block.Data.Count - Segment.End);

        public void Write(byte data)
        {
            if (IsDefault)
            {
                return;
            }

            Debug.Assert(_block != null);
            Debug.Assert(_segment.Next == null);
            Debug.Assert(_segment.End == _index);

            var pool = _block.Pool;
            var segment = _segment;
            var block = _block;
            var blockIndex = _index;

            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            if (bytesLeftInBlock == 0)
            {
                var nextBlock = pool.Lease();
                var nextSegment = new LinkedSegment(nextBlock);
                segment.End = blockIndex;
                Volatile.Write(ref segment.Next, nextSegment);
                segment = nextSegment;
                block = nextBlock;

                blockIndex = block.Data.Offset;
            }

            block.Array[blockIndex] = data;

            blockIndex++;

            segment.End = blockIndex;
            _segment = segment;
            _block = block;
            _index = blockIndex;
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (IsDefault)
            {
                return;
            }

            Debug.Assert(_block != null);
            Debug.Assert(_segment.Next == null);
            Debug.Assert(_segment.End == _index);

            var pool = _block.Pool;
            var segment = _segment;
            var block = _block;
            var blockIndex = _index;

            var bufferIndex = offset;
            var remaining = count;
            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            while (remaining > 0)
            {
                if (bytesLeftInBlock == 0)
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
            _segment = segment;
            _block = block;
            _index = blockIndex;
        }

        public void Append(ReadableBuffer begin, ReadableBuffer end)
        {
            Debug.Assert(_block != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            _block.Next = begin.Block;
            _block = end.Block;
            _index = end.Block.End;
        }

        public void UpdateWritten(int bytesWritten)
        {
            Debug.Assert(_block != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            var block = _block;
            var blockIndex = _index + bytesWritten;

            Debug.Assert(blockIndex <= block.Data.Offset + block.Data.Count);

            block.End = blockIndex;
            _block = block;
            _index = blockIndex;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < (_block.End - _index); i++)
            {
                builder.Append(_block.Array[i + _index].ToString("X2"));
                builder.Append(" ");
            }
            return builder.ToString();
        }
    }
}
