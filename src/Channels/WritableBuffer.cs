// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that can write a sequential series of bytes.
    /// </summary>
    public struct WritableBuffer : IOutput
    {
        private WritableChannel _writableChannel;

        internal WritableBuffer(WritableChannel writableChannel)
        {
            _writableChannel = writableChannel;
        }

        /// <summary>
        /// Available memory.
        /// </summary>
        public Memory<byte> Memory => _writableChannel.Channel.Memory;

        /// <summary>
        /// Returns the number of bytes currently written and uncommitted.
        /// </summary>
        public int BytesWritten => AsReadableBuffer().Length;

        Span<byte> IOutput.Buffer => Memory;

        void IOutput.Enlarge(int desiredBufferLength) => Ensure(desiredBufferLength);

        /// <summary>
        /// Obtain a readable buffer over the data written but uncommitted to this buffer.
        /// </summary>
        public ReadableBuffer AsReadableBuffer()
        {
            return _writableChannel.Channel.AsReadableBuffer();
        }

        /// <summary>
        /// Ensures the specified number of bytes are available.
        /// Will assign more memory to the <see cref="WritableBuffer"/> if requested amount not currently available.
        /// </summary>
        /// <param name="count">number of bytes</param>
        /// <remarks>
        /// Used when writing to <see cref="Memory"/> directly. 
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// More requested than underlying <see cref="IBufferPool"/> can allocate in a contiguous block.
        /// </exception>
        public void Ensure(int count = 1)
        {
            _writableChannel.Channel.Ensure(count);
        }

        /// <summary>
        /// Appends the <see cref="ReadableBuffer"/> to the <see cref="WritableBuffer"/> in-place without copies.
        /// </summary>
        /// <param name="buffer">The <see cref="ReadableBuffer"/> to append</param>
        public void Append(ref ReadableBuffer buffer)
        {
            _writableChannel.Channel.Append(ref buffer);
        }

        /// <summary>
        /// Moves forward the underlying <see cref="IWritableChannel"/>'s write cursor but does not commit the data.
        /// </summary>
        /// <param name="bytesWritten">number of bytes to be marked as written.</param>
        /// <remarks>Forwards the start of available <see cref="Memory"/> by <paramref name="bytesWritten"/>.</remarks>
        /// <exception cref="ArgumentException"><paramref name="bytesWritten"/> is larger than the current data available data.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytesWritten"/> is negative.</exception>
        public void Advance(int bytesWritten)
        {
            _writableChannel.Channel.AdvanceWriter(bytesWritten);
        }

        /// <summary>
        /// Commits all outstanding written data to the underlying <see cref="IWritableChannel"/> so they can be read
        /// and seals the <see cref="WritableBuffer"/> so no more data can be committed.
        /// </summary>
        /// <remarks>
        /// While an on-going conncurent read may pick up the data, <see cref="FlushAsync"/> should be called to signal the reader.
        /// </remarks>
        public void Commit()
        {
            _writableChannel.Commit();
        }

        /// <summary>
        /// Signals the <see cref="IReadableChannel"/> data is available.
        /// Will <see cref="Commit"/> if necessary.
        /// </summary>
        /// <returns>A task that completes when the data is fully flushed.</returns>
        public Task FlushAsync()
        {
            return _writableChannel.FlushAsync();
        }
    }
}
