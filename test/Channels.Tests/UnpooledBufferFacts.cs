using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Channels.Tests
{
    public class UnpooledBufferFacts
    {
        [Fact]
        public async Task CanConsumeData()
        {
            var channel = new UnpooledChannel();
        }
    }
}
