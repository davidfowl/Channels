using System;
using System.Text;
using System.Text.Formatting;
using System.Threading.Tasks;

namespace Channels.Text.Primitives
{
    public static class WritableChannelExtensions
    {
        public static WritableChannelFormatter GetFormatter(this IPipelineWriter channel, EncodingData formattingData)
        {
            return new WritableChannelFormatter(channel, formattingData);
        }
    }
}
