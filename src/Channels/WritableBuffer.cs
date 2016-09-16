// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that can write a sequential series of bytes.
    /// </summary>
    public struct WritableBuffer
    {
        private IBufferPool _pool;
        private Channel _channel;

        // the "tail" is the **end** of the data that we're writing - essentially the
        // write-head
        private PooledBufferSegment _tail;
        private int _tailIndex;

        // the "head" is the **start** of the data that we're writing; from the head,
        // you can iterate forward via .Next to access all of the data written
        // in this session
        private PooledBufferSegment _head;
        private int _headIndex;

        private bool _comitted;
        private int _bufferSize;

        internal WritableBuffer(Channel channel, IBufferPool pool, PooledBufferSegment segment, int bufferSize)
        {
            _channel = channel;
            _pool = pool;

            _tail = segment;
            _tailIndex = segment?.End ?? 0;

            _head = segment;
            _headIndex = _tailIndex;

            _comitted = false;
            _bufferSize = bufferSize;
        }

        internal PooledBufferSegment Head => _head;

        internal int HeadIndex => _headIndex;

        internal PooledBufferSegment Tail => _tail;

        internal int TailIndex => _tailIndex;

        internal bool IsDefault => _tail == null;

        /// <summary>
        /// Free memory
        /// </summary>
        public Span<byte> Memory => IsDefault ? Span<byte>.Empty : _tail.Buffer.Data.Slice(_tail.End, _tail.Buffer.Data.Length - _tail.End);

        /// <summary>
        /// Obtain a readable buffer over the data written to this buffer
        /// </summary>
        public ReadableBuffer AsReadableBuffer()
        {
            if (Head == null)
            {
                return new ReadableBuffer(); // empty
            }

            return new ReadableBuffer(null, new ReadCursor(Head, HeadIndex), new ReadCursor(Tail, TailIndex), isOwner: false);
        }

        /// <summary>
        /// Ensures the specified number of bytes are available
        /// </summary>
        /// <param name="count"></param>
        public void Ensure(int count = 1)
        {
            if (count > _bufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"Cannot allocate more than {_bufferSize} bytes in a single buffer");
            }

            if (_tail == null)
            {
                _tail = new PooledBufferSegment(_pool.Lease(_bufferSize));
                _tailIndex = _tail.End;
                _head = _tail;
                _headIndex = _tail.Start;
            }

            Debug.Assert(_tail.Next == null);
            Debug.Assert(_tail.End == _tailIndex);

            var segment = _tail;
            var buffer = _tail.Buffer;
            var bufferIndex = _tailIndex;
            var bytesLeftInBuffer = buffer.Data.Length - bufferIndex;

            // If inadequate bytes left or if the segment is readonly
            if (bytesLeftInBuffer == 0 || bytesLeftInBuffer < count || segment.ReadOnly)
            {
                var nextBuffer = _pool.Lease(_bufferSize);
                var nextSegment = new PooledBufferSegment(nextBuffer);
                segment.End = bufferIndex;
                segment.Next = nextSegment;
                segment = nextSegment;
                buffer = nextBuffer;

                bufferIndex = 0;

                segment.End = bufferIndex;
                segment.Buffer = buffer;
                _tail = segment;
                _tailIndex = bufferIndex;
            }
        }

        /// <summary>
        /// Writes the source <see cref="Span{Byte}"/> to the <see cref="WritableBuffer"/>.
        /// </summary>
        /// <param name="source">The <see cref="Span{Byte}"/> to write</param>
        public void Write(Span<byte> source)
        {
            if (IsDefault)
            {
                Ensure();
            }

            // Fast path, try copying to the available memory directly
            if (source.TryCopyTo(Memory))
            {
                CommitBytes(source.Length);
                return;
            }

            var remaining = source.Length;
            var offset = 0;

            while (remaining > 0)
            {
                var writable = Math.Min(remaining, Memory.Length);

                Ensure(writable);

                if (writable == 0)
                {
                    continue;
                }

                source.Slice(offset, writable).TryCopyTo(Memory);

                remaining -= writable;
                offset += writable;

                CommitBytes(writable);
            }
        }

        /// <summary>
        /// Appends the <see cref="ReadableBuffer"/> to the <see cref="WritableBuffer"/> in-place without copies.
        /// </summary>
        /// <param name="buffer">The <see cref="ReadableBuffer"/> to append</param>
        public void Append(ref ReadableBuffer buffer)
        {
            PooledBufferSegment clonedEnd;
            var clonedBegin = PooledBufferSegment.Clone(buffer.Start, buffer.End, out clonedEnd);

            if (_tail == null)
            {
                _head = clonedBegin;
                _headIndex = clonedBegin.Start;
            }
            else
            {
                Debug.Assert(_tail.Next == null);
                Debug.Assert(_tail.End == _tailIndex);

                _tail.Next = clonedBegin;
            }

            _tail = clonedEnd;
            _tailIndex = clonedEnd.End;
        }

        /// <summary>
        /// Commits the written data to the underlying <see cref="IWritableChannel"/>.
        /// </summary>
        public void Commit()
        {
            if (!_comitted)
            {
                _channel.Append(this);

                _comitted = true;
            }
        }

        /// <summary>
        /// Writes the committed data to the underlying <see cref="IWritableChannel"/>.
        /// </summary>
        /// <returns>A task that completes when the data is written</returns>
        public Task FlushAsync()
        {
            Commit();
            return _channel.CompleteWriteAsync();
        }

        /// <summary>
        /// Updates the number of written bytes in the <see cref="WritableBuffer"/>.
        /// </summary>
        /// <param name="bytesWritten">number of bytes written to available memory</param>
        public void CommitBytes(int bytesWritten)
        {
            if (bytesWritten > 0)
            {
                Debug.Assert(_tail != null);
                Debug.Assert(!_tail.ReadOnly);
                Debug.Assert(_tail.Next == null);
                Debug.Assert(_tail.End == _tailIndex);

                var buffer = _tail.Buffer;
                var bufferIndex = _tailIndex + bytesWritten;

                Debug.Assert(bufferIndex <= buffer.Data.Length);

                _tail.End = bufferIndex;
                _tailIndex = bufferIndex;
            }
            else if (bytesWritten < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesWritten));
            } // and if zero, just do nothing; don't need to validate tail etc
        }
    }
}
