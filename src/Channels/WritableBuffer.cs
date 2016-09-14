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
        private IBufferPool _pool;
        private Channel _channel;

        private BufferSegment _tail;
        private int _tailIndex;

        private BufferSegment _head;
        private int _headIndex;

        private bool _comitted;

        internal WritableBuffer(Channel channel, IBufferPool pool, BufferSegment segment)
        {
            _channel = channel;
            _pool = pool;

            _tail = segment;
            _tailIndex = segment?.End ?? 0;

            _head = segment;
            _headIndex = _tailIndex;

            _comitted = false;
        }

        internal BufferSegment Head => _head;

        internal int HeadIndex => _headIndex;

        internal BufferSegment Tail => _tail;

        internal int TailIndex => _tailIndex;

        internal bool IsDefault => _tail == null;

        public Span<byte> Memory => IsDefault ? Span<byte>.Empty : _tail.Buffer.Data.Slice(_tail.End, _tail.Buffer.Data.Length - _tail.End);

        public void Ensure(int count = 1)
        {
            if (_tail == null)
            {
                _tail = new BufferSegment(_pool.Lease());
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
                var nextBuffer = _pool.Lease();
                var nextSegment = new BufferSegment(nextBuffer);
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

        public void Append(ref ReadableBuffer buffer)
        {
            BufferSegment clonedEnd;
            var clonedBegin = BufferSegment.Clone(buffer.Start, buffer.End, out clonedEnd);

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
