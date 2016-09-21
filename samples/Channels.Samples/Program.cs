using Channels.Samples.Framing;

namespace Channels.Samples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // AspNetHttpServerSample.Run();
            RawLibuvHttpServerSample.Run();
            // ProtocolHandling.Run();
        }
    }
}
