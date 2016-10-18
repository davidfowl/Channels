using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Tests.Internal
{
    public class LoopbackChannel
    {
        IChannel _clientChannel;
        IChannel _serverChannel;

        public LoopbackChannel(ChannelFactory factory)
        {
            var backChannel1 = factory.CreateChannel();
            var backChannel2 = factory.CreateChannel();
            
            _clientChannel = new TestChannel(backChannel1, backChannel2);
            _serverChannel = new TestChannel(backChannel2, backChannel1);
        }
        
        public IChannel ServerChannel => _serverChannel;
        public IChannel ClientChannel => _clientChannel;

        class TestChannel : IChannel
        {
            Channel _inChannel;
            Channel _outChannel;

            public TestChannel(Channel inChannel, Channel outChannel)
            {
                _inChannel = inChannel;
                _outChannel = outChannel;
            }

            public IReadableChannel Input => _inChannel;
            public IWritableChannel Output => _outChannel;

            public void Dispose()
            {
            }
        }
    }
}
