using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Channels.Tests
{
    public class TestingIChannel: IChannel
    {
        private Channel _inputChannel;
        private Channel _outputChannel;

        public IReadableChannel Input => _inputChannel;
        public IWritableChannel Output => _outputChannel;
        public Channel RawInput => _inputChannel;
        public Channel RawOutput => _outputChannel;

        public TestingIChannel(ChannelFactory factory)
            :this(factory.CreateChannel(), factory.CreateChannel())
        {
        }

        public TestingIChannel(Channel inputChannel, Channel outputChannel)
        {
            _inputChannel = inputChannel;
            _outputChannel = outputChannel;
        }
        
        public void Dispose() {}
    }
}