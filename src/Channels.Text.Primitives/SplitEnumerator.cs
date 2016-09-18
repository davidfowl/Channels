﻿using System;
using System.Collections;
using System.Collections.Generic;

namespace Channels.Text.Primitives
{
    /// <summary>
    /// Supports a simple iteration over a sequence of buffers from a Split operation.
    /// </summary>
    public struct SplitEnumerator : IEnumerator<ReadableBuffer>
    {
        private readonly byte _delimiter;
        private ReadableBuffer _current, _remainder;
        internal SplitEnumerator(ReadableBuffer remainder, byte delimiter)
        {
            _current = default(ReadableBuffer);
            _remainder = remainder;
            _delimiter = delimiter;
        }

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator.
        /// </summary>
        public ReadableBuffer Current => _current;

        object IEnumerator.Current => _current;

        /// <summary>
        /// Releases all resources owned by the instance
        /// </summary>
        public void Dispose() { }

        void IEnumerator.Reset()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Advances the enumerator to the next element of the collection.
        /// </summary>
        public bool MoveNext()
        {
            ReadCursor cursor;
            if (_remainder.TrySliceTo(_delimiter, out _current, out cursor))
            {
                _remainder = _remainder.Slice(cursor).Slice(1);
                return true;
            }
            // once we're out of splits, yield whatever is left
            if (_remainder.IsEmpty)
            {
                return false;
            }
            _current = _remainder;
            _remainder = default(ReadableBuffer);
            return true;
        }
    }
}
