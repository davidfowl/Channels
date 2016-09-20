// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Channels
{
    /// <summary>
    /// Represents a buffer that can write a sequential series of bytes.
    /// </summary>
    public struct WritableBuffer
    {
        private Channel _channel;

        internal WritableBuffer(Channel channel)
        {
            _channel = channel;
        }

        /// <summary>
        /// Free memory
        /// </summary>
        public Span<byte> Memory => _channel.Memory;

        /// <summary>
        /// Obtain a readable buffer over the data written to this buffer
        /// </summary>
        public ReadableBuffer AsReadableBuffer()
        {
            return _channel.AsReadableBuffer();
        }

        /// <summary>
        /// Ensures the specified number of bytes are available
        /// </summary>
        /// <param name="count"></param>
        public void Ensure(int count = 1)
        {
            _channel.Ensure(count);
        }

        /// <summary>
        /// Appends the <see cref="ReadableBuffer"/> to the <see cref="WritableBuffer"/> in-place without copies.
        /// </summary>
        /// <param name="buffer">The <see cref="ReadableBuffer"/> to append</param>
        public void Append(ref ReadableBuffer buffer)
        {
            _channel.Append(ref buffer);
        }

        /// <summary>
        /// Commits the written data to the underlying <see cref="IWritableChannel"/>.
        /// </summary>
        public void Commit()
        {
            _channel.Commit();
        }

        /// <summary>
        /// Writes the committed data to the underlying <see cref="IWritableChannel"/>.
        /// </summary>
        /// <returns>A task that completes when the data is written</returns>
        public Task FlushAsync()
        {
            return _channel.FlushAsync();
        }

        /// <summary>
        /// Updates the number of written bytes in the <see cref="WritableBuffer"/>.
        /// </summary>
        /// <param name="bytesWritten">number of bytes written to available memory</param>
        public void CommitBytes(int bytesWritten)
        {
            _channel.CommitBytes(bytesWritten);
        }
    }
}
