﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that can read a sequential series of bytes.
    /// </summary>
    public struct PreservedBuffer : IDisposable
    {
        private ReadableBuffer _buffer;

        internal PreservedBuffer(ref ReadableBuffer buffer)
        {
            _buffer = buffer;
        }

        /// <summary>
        /// Returns the preserved <see cref="Buffer"/>.
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
                returnSegment?.Dispose();

                if (returnSegment == returnEnd)
                {
                    break;
                }
            }

            _buffer.ClearCursors();
        }

    }
}
