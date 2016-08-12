// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;

namespace Channels
{
    // TODO: Pool segments?
    internal class LinkedSegment : IDisposable
    {
        /// <summary>
        /// Block tracking object used by the byte buffer memory pool.
        /// </summary>
        public MemoryPoolBlock Block;

        /// <summary>
        /// The Start represents the offset into Array where the range of "active" bytes begins. At the point when the block is leased
        /// the Start is guaranteed to be equal to Array.Offset. The value of Start may be assigned anywhere between Data.Offset and
        /// Data.Offset + Data.Count, and must be equal to or less than End.
        /// </summary>
        public int Start;

        /// <summary>
        /// The End represents the offset into Array where the range of "active" bytes ends. At the point when the block is leased
        /// the End is guaranteed to be equal to Array.Offset. The value of Start may be assigned anywhere between Data.Offset and
        /// Data.Offset + Data.Count, and must be equal to or less than End.
        /// </summary>
        public volatile int End;

        /// <summary>
        /// Reference to the next block of data when the overall "active" bytes spans multiple blocks. At the point when the block is
        /// leased Next is guaranteed to be null. Start, End, and Next are used together in order to create a linked-list of discontiguous 
        /// working memory. The "active" memory is grown when bytes are copied in, End is increased, and Next is assigned. The "active" 
        /// memory is shrunk when bytes are consumed, Start is increased, and blocks are returned to the pool.
        /// </summary>
        public LinkedSegment Next;


        /// <summary>
        /// If true, data should not be written into the backing block after the End offset. Data between start and end should never be modified
        /// since this would break cloning.
        /// </summary>
        public bool ReadOnly;

        // Leasing ctor
        public LinkedSegment(MemoryPoolBlock block)
        {
            Block = block;
            Start = block.Data.Offset;
            End = block.Data.Offset;
        }

        // Cloning ctor
        private LinkedSegment(MemoryPoolBlock block, int start, int end)
        {
            Block = block;
            Start = start;
            End = end;

            block.AddReference();
            ReadOnly = true;
        }

        public void Dispose()
        {
            Block.RemoveReference();
        }


        /// <summary>
        /// ToString overridden for debugger convenience. This displays the "active" byte information in this block as ASCII characters.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < (End - Start); i++)
            {
                builder.Append(Block.Array[i + Start].ToString("X2"));
                builder.Append(" ");
            }
            return builder.ToString();
        }

        public static LinkedSegment Clone(ReadableBuffer beginBuffer, ReadableBuffer endBuffer)
        {
            var beginOrig = beginBuffer.Segment;
            var endOrig = endBuffer.Segment;

            if (beginOrig == endOrig)
            {
                return new LinkedSegment(beginOrig.Block, beginBuffer.Index, endBuffer.Index);
            }

            var beginClone = new LinkedSegment(beginOrig.Block, beginBuffer.Index, beginOrig.End);
            var endClone = beginClone;

            beginOrig = beginOrig.Next;

            while (beginOrig != endOrig)
            {
                endClone.Next = new LinkedSegment(beginOrig.Block, beginOrig.Start, beginOrig.End);

                endClone = endClone.Next;
                beginOrig = beginOrig.Next;
            }

            endClone.Next = new LinkedSegment(endOrig.Block, endOrig.Start, endBuffer.Index);

            return beginClone;
        }
    }
}
