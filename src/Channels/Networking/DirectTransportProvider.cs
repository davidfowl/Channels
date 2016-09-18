using System;
using System.Collections;
using System.Threading.Tasks;

namespace Channels.Networking
{
    /// <summary>
    /// A direct transport that connects a client and server without going through any networking layer
    /// </summary>
    public class DirectTransportProvider : TransportProvider
    {
        private Hashtable servers = new Hashtable();

        /// <summary>
        /// Open a client connection to the designated resource
        /// </summary>
        public override Task<IChannel> ConnectAsync(string configuration)
        {
            if (configuration == null) configuration = "";
            var server = (DirectTransportServer)servers[configuration];
            if (server == null) throw new InvalidOperationException($"No server listening for: '{configuration}'");

            return Task.FromResult(server.AddClient());
        }
        /// <summary>
        /// Create a server instance listening to the designated resource
        /// </summary>
        public override Task<IDisposable> StartServerAsync(string configuration, Action<IChannel> callback)
        {
            DirectTransportServer server;
            lock (servers) // planning to mutate, so lock
            {
                server = (DirectTransportServer)servers[configuration];
                if (server != null) throw new InvalidOperationException($"Server already listening for: '{configuration}'");

                server = new DirectTransportServer(this, configuration, callback);
                servers[configuration] = server;
            }
            return Task.FromResult<IDisposable>(server);
        }
        private void Remove(string key)
        {
            lock (servers) // planning to mutate, so lock
            {
                servers.Remove(key);
            }
        }

        private class DirectTransportServer : IDisposable
        {
            private Action<IChannel> callback;
            private string key;
            private DirectTransportProvider provider;

            public DirectTransportServer(DirectTransportProvider provider, string  key, Action<IChannel> callback)
            {
                this.provider = provider;
                this.key = key;
                this.callback = callback;
            }

            public void Dispose()
            {
                provider.Remove(key);
                provider = null;
                key = null;
            }

            internal IChannel AddClient()
            {
                var clientToServer = provider.CreateChannel();
                var serverToClient = provider.CreateChannel();

                var client = new DirectChannel(serverToClient, clientToServer);
                var server = new DirectChannel(clientToServer, serverToClient);
                callback(server);
                return client;
            }
            private class DirectChannel : IChannel
            {
                IReadableChannel input;
                IWritableChannel output;

                public DirectChannel(IReadableChannel input, IWritableChannel output)
                {
                    this.input = input;
                    this.output = output;
                }
                public IReadableChannel Input => input;

                public IWritableChannel Output => output;

                public void Dispose() { }
            }
        }

        private Channel CreateChannel() => ChannelFactory.CreateChannel();

        /// <summary>
        /// Releases all resources owned by this object
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                foreach (DirectTransportServer server in servers)
                {
                    server?.Dispose();
                    servers = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
