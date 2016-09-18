using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace Channels.Networking
{
    /// <summary>
    /// Represents a transport that is able to establish connections from client to server,
    /// or to start a listener to operate as a server
    /// </summary>
    public abstract class TransportProvider : IDisposable
    {
        private ChannelFactory channelFactory;

        /// <summary>
        /// Provides access to the channel factory for this provider
        /// </summary>
        /// <remarks>This factory is lazily instantiated</remarks>
        protected ChannelFactory ChannelFactory => channelFactory ?? (channelFactory = new ChannelFactory());

        /// <summary>
        /// Open a client connection to the designated resource
        /// </summary>
        public abstract Task<IChannel> ConnectAsync(string configuration);
        /// <summary>
        /// Create a server instance listening to the designated resource
        /// </summary>
        public abstract Task<IDisposable> StartServerAsync(string configuration, Action<IChannel> callback);

        /// <summary>
        /// Releases all resources owned by this object
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        /// <summary>
        /// Releases all resources owned by this object
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                channelFactory?.Dispose();
                channelFactory = null;
            }
        }

        /// <summary>
        /// Parse the configuration as an IPEndPoint
        /// </summary>
        public static ValueTask<IPEndPoint> ParseIPEndPoint(string configuration, int defaultPort = -1)
        {
            if (string.IsNullOrWhiteSpace(configuration)) throw new ArgumentException(nameof(configuration));
            configuration = configuration.Trim();

            // try IP by itself first; this is necessary to help distinguish between IPv6 without port which looks
            // confusingly similar to IPv4:port
            IPAddress addr;
            if (!configuration.Contains("]:") && IPAddress.TryParse(configuration, out addr))
            {
                if (defaultPort < 0)
                {
                    throw new ArgumentException($"No port specified", nameof(configuration));
                }
                return new ValueTask<IPEndPoint>(new IPEndPoint(addr, defaultPort));
            }

            // try to resolve a *:port
            int port;
            int colonIndex = configuration.LastIndexOf(':');
            string endpointString = colonIndex < 0 ? configuration.Trim() : configuration.Substring(0, colonIndex).Trim(),
                portString = colonIndex < 0 ? "" : configuration.Substring(colonIndex + 1).Trim();

            if (string.IsNullOrWhiteSpace(portString))
            {
                if (defaultPort < 0)
                {
                    throw new ArgumentException($"No port specified", nameof(configuration));
                }
                port = defaultPort;
            }
            else
            {
                if (!int.TryParse(portString, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                {
                    throw new ArgumentException($"Cannot parse port '{portString}'", nameof(configuration));
                }
            }

            // is the remainder on the left an IP address?
            if(IPAddress.TryParse(endpointString, out addr))
            {
                return new ValueTask<IPEndPoint>(new IPEndPoint(addr, port));
            }

            // otherwise, use DNS lookup
            return new ValueTask<IPEndPoint>(ResolveDnsAndCreateIPEndPoint(endpointString, port));
        }

        private static async Task<IPEndPoint> ResolveDnsAndCreateIPEndPoint(string endpoint, int port)
        {
            var addresses = await Dns.GetHostAddressesAsync(endpoint);
            if(addresses.Length == 0)
            {
                throw new InvalidOperationException($"Unable to resolve endpoint: '{endpoint}'");
            }
            return new IPEndPoint(addresses[0], port);
        }
    }
}
