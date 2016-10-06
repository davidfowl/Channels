// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that can read a sequential series of bytes.
    /// </summary>
    public struct PreservedBuffer : IDisposable
    {
        private readonly IBufferSegmentFactory _segmentFactory;
        private ReadableBuffer _buffer;

        internal PreservedBuffer(ref ReadableBuffer buffer, IBufferSegmentFactory segmentFactory)
        {
            _segmentFactory = segmentFactory;

            var begin = buffer.Start;
            var end = buffer.End;

            BufferSegment segmentTail;
            var segmentHead = BufferSegment.Clone(_segmentFactory, begin, end, out segmentTail);

            begin = new ReadCursor(segmentHead);
            end = new ReadCursor(segmentTail, segmentTail.End);


            _buffer = new ReadableBuffer(begin, end);
        }

        /// <summary>
        /// Returns the preserved <see cref="ReadableBuffer"/>.
        /// </summary>
        public ReadableBuffer Buffer => _buffer;

        /// <summary>
        /// Dispose the preserved buffer.
        /// </summary>
        public void Dispose()
        {
            var returnStart = _buffer.Start.Segment;
            var returnEnd = _buffer.End.Segment;

            while (true)
            {
                var returnSegment = returnStart;
                returnStart = returnStart?.Next;
                if (returnSegment != null)
                {
                    _segmentFactory.Dispose(returnSegment);
                }

                if (returnSegment == returnEnd)
                {
                    break;
                }
            }

            _buffer.ClearCursors();
        }

    }
}
