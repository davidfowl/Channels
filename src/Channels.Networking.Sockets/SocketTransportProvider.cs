using System;
using System.Threading.Tasks;

namespace Channels.Networking.Sockets
{
    /// <summary>
    /// A TransportProvider using managed sockets (System.Net.Sockets.Socket)
    /// </summary>
    public class SocketTransportProvider : TransportProvider
    {
        /// <summary>
        /// Open a client connection to the designated resource
        /// </summary>
        public async override Task<IChannel> ConnectAsync(string configuration)
        {
            var endpoint = await ParseIPEndPoint(configuration);
            return await SocketConnection.ConnectAsync(endpoint, ChannelFactory);
        }
        /// <summary>
        /// Create a server instance listening to the designated resource
        /// </summary>
        public override async Task<IDisposable> StartServerAsync(string configuration, Action<IChannel> callback)
        {
            var endpoint = await ParseIPEndPoint(configuration);
            var server = new SocketListener(ChannelFactory);
            server.OnConnection(callback);
            server.Start(endpoint);
            return server;
        }
    }
}
