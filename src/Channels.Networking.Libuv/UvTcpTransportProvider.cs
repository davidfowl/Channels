using System;
using System.Threading.Tasks;

namespace Channels.Networking.Libuv
{
    /// <summary>
    /// A TransportProvider using libuv
    /// </summary>
    public class UvTcpTransportProvider : TransportProvider
    {
        UvThread thread = new UvThread();
        /// <summary>
        /// Open a client connection to the designated resource
        /// </summary>
        public async override Task<IChannel> ConnectAsync(string configuration)
        {
            var endpoint = await ParseIPEndPoint(configuration);
            return await new UvTcpClient(thread, endpoint).ConnectAsync();
        }

        /// <summary>
        /// Create a server instance listening to the designated resource
        /// </summary>
        public override async Task<IDisposable> StartServerAsync(string configuration, Action<IChannel> callback)
        {
            var endpoint = await ParseIPEndPoint(configuration);
            var server = new UvTcpListener(thread, endpoint);
            server.OnConnection(callback);
            server.Start();
            return server;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                thread?.Dispose();
                thread = null;
            }
        }
    }
}
