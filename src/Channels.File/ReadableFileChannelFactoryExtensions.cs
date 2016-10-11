using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.File
{
    public static class ReadableFileChannelFactoryExtensions
    {
        public static IReadableChannel ReadFile(this ChannelFactory factory, string path)
        {
            var channel = factory.CreateChannel();

            var file = new ReadableFileChannel(channel);
            file.OpenReadFile(path);
            return file;
        }
    }
}
