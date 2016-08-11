// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Channels
{
    public struct WritableBuffer
    {
        private static readonly int _vectorSpan = Vector<byte>.Count;

        private MemoryPoolBlock _block;
        private int _index;

        public WritableBuffer(MemoryPoolBlock block)
        {
            _block = block;
            _index = _block?.Start ?? 0;
        }
        public WritableBuffer(MemoryPoolBlock block, int index)
        {
            _block = block;
            _index = index;
        }

        internal MemoryPoolBlock Block => _block;

        internal int Index => _index;

        public bool IsDefault => _block == null;

        public IntPtr DataArrayPtr => _block.DataArrayPtr + _block.End;

        public int WritableBytes => _block.Data.Offset + _block.Data.Count - _block.End;

        public bool IsEnd
        {
            get
            {
                if (_block == null)
                {
                    return true;
                }
                else if (_index < _block.End)
                {
                    return false;
                }
                else
                {
                    var block = _block.Next;
                    while (block != null)
                    {
                        if (block.Start < block.End)
                        {
                            return false; // subsequent block has data - IsEnd is false
                        }
                        block = block.Next;
                    }
                    return true;
                }
            }
        }

        public void Write(byte data)
        {
            if (IsDefault)
            {
                return;
            }

            Debug.Assert(_block != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            var pool = _block.Pool;
            var block = _block;
            var blockIndex = _index;

            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;

            if (bytesLeftInBlock == 0)
            {
                var nextBlock = pool.Lease();
                block.End = blockIndex;
                Volatile.Write(ref block.Next, nextBlock);
                block = nextBlock;

                blockIndex = block.Data.Offset;
                bytesLeftInBlock = block.Data.Count;
            }

            block.Array[blockIndex] = data;

            blockIndex++;

            block.End = blockIndex;
            _block = block;
            _index = blockIndex;
        }

        public void CopyFrom(byte[] data)
        {
            CopyFrom(data, 0, data.Length);
        }

        public void CopyFrom(ArraySegment<byte> buffer)
        {
            CopyFrom(buffer.Array, buffer.Offset, buffer.Count);
        }

        public void CopyFrom(byte[] data, int offset, int count)
        {
            if (IsDefault)
            {
                return;
            }

            Debug.Assert(_block != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            var pool = _block.Pool;
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
                    block.End = blockIndex;
                    Volatile.Write(ref block.Next, nextBlock);
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

            block.End = blockIndex;
            _block = block;
            _index = blockIndex;
        }

        public unsafe void CopyFromAscii(string data)
        {
            if (IsDefault)
            {
                return;
            }

            Debug.Assert(_block != null);
            Debug.Assert(_block.Next == null);
            Debug.Assert(_block.End == _index);

            var pool = _block.Pool;
            var block = _block;
            var blockIndex = _index;
            var length = data.Length;

            var bytesLeftInBlock = block.Data.Offset + block.Data.Count - blockIndex;
            var bytesLeftInBlockMinusSpan = bytesLeftInBlock - 3;

            fixed (char* pData = data)
            {
                var input = pData;
                var inputEnd = pData + length;
                var inputEndMinusSpan = inputEnd - 3;

                while (input < inputEnd)
                {
                    if (bytesLeftInBlock == 0)
                    {
                        var nextBlock = pool.Lease();
                        block.End = blockIndex;
                        Volatile.Write(ref block.Next, nextBlock);
                        block = nextBlock;

                        blockIndex = block.Data.Offset;
                        bytesLeftInBlock = block.Data.Count;
                        bytesLeftInBlockMinusSpan = bytesLeftInBlock - 3;
                    }

                    var output = (block.DataFixedPtr + block.End);
                    var copied = 0;
                    for (; input < inputEndMinusSpan && copied < bytesLeftInBlockMinusSpan; copied += 4)
                    {
                        *(output) = (byte)*(input);
                        *(output + 1) = (byte)*(input + 1);
                        *(output + 2) = (byte)*(input + 2);
                        *(output + 3) = (byte)*(input + 3);
                        output += 4;
                        input += 4;
                    }
                    for (; input < inputEnd && copied < bytesLeftInBlock; copied++)
                    {
                        *(output++) = (byte)*(input++);
                    }

                    blockIndex += copied;
                    bytesLeftInBlockMinusSpan -= copied;
                    bytesLeftInBlock -= copied;
                }
            }

            block.End = blockIndex;
            _block = block;
            _index = blockIndex;
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
