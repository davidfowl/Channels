using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels
{
    public static class DefaultWritableBufferExtensions
    {
        /// <summary>
        /// Writes the source <see cref="Span{Byte}"/> to the <see cref="WritableBuffer"/>.
        /// </summary>
        /// <param name="buffer">The <see cref="WritableBuffer"/></param>
        /// <param name="source">The <see cref="Span{Byte}"/> to write</param>
        public static void Write(this WritableBuffer buffer, Span<byte> source)
        {
            if (buffer.Memory.IsEmpty)
            {
                buffer.Ensure();
            }

            // Fast path, try copying to the available memory directly
            if (source.TryCopyTo(buffer.Memory))
            {
                buffer.CommitBytes(source.Length);
                return;
            }

            var remaining = source.Length;
            var offset = 0;

            while (remaining > 0)
            {
                var writable = Math.Min(remaining, buffer.Memory.Length);

                buffer.Ensure(writable);

                if (writable == 0)
                {
                    continue;
                }

                source.Slice(offset, writable).TryCopyTo(buffer.Memory);

                remaining -= writable;
                offset += writable;

                buffer.CommitBytes(writable);
            }
        }
    }
}
