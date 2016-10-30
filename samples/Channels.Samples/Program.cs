using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Channels.Samples.Framing;
using Channels.Text.Primitives;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            AspNetHttpServerSample.Run();
            // RawLibuvHttpServerSample.Run();
            // ProtocolHandling.Run();
        }
    }
}
