using System;
using System.Text;
using System.Text.Formatting;
using System.Threading.Tasks;

namespace Channels.Text.Primitives
{
    public static class WritableChannelExtensions
    {
        public static WriteableChannelFormatter GetFormatter(this IWritableChannel channel, EncodingData formattingData)
        {
            return new WriteableChannelFormatter(channel, formattingData);
        }
    }
}
