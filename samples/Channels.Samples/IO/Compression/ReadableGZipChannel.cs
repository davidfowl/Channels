using System;
using System.Collections.Generic;
using System.IO.Compression;

namespace Channels.Samples.IO.Compression
{
    public class ReadableGZipChannel : ReadableDeflateChannel
    {
        public ReadableGZipChannel(IReadableChannel inner, MemoryPool pool) : base(inner, ZLibNative.GZip_DefaultWindowBits, pool)
        {
        }
    }
}
