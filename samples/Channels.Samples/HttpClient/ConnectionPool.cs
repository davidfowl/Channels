using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Channels.Networking.Libuv;

namespace Channels.Samples
{
    public class ConnectionPool : IDisposable
    {
        private readonly UvThread _thread = new UvThread();

        private ConcurrentDictionary<string, Pool> _poolMap = new ConcurrentDictionary<string, Pool>();

        private const int MaxConcurrency = 1;

        private bool _disposed;

        public async Task<HttpClientConnection> GetConnectionAsync(HttpRequestMessage request)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            var key = request.RequestUri.GetComponents(UriComponents.HostAndPort, UriFormat.SafeUnescaped);

            var pool = _poolMap.GetOrAdd(key, s => new Pool(MaxConcurrency));

            await pool.Concurrency.WaitAsync();

            HttpClientConnection connection;
            if (!pool.Connections.TryDequeue(out connection))
            {
                connection = await CreateNewConnectionAsync(request);
                connection.Pool = pool;
            }

            return connection;
        }

        public void Return(HttpClientConnection connection)
        {
            connection.Pool.Connections.Enqueue(connection);

            connection.Pool.Concurrency.Release();
        }


        private async Task<HttpClientConnection> CreateNewConnectionAsync(HttpRequestMessage request)
        {
            var addresses = await Dns.GetHostAddressesAsync(request.RequestUri.Host);
            var port = request.RequestUri.Port;

            var address = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            var client = new UvTcpClient(_thread, new IPEndPoint(address, port));
            var connection = await client.ConnectAsync();
            return new HttpClientConnection(connection);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                foreach (var group in _poolMap)
                {
                    while (group.Value.Connections.Count > 0)
                    {
                        HttpClientConnection connection;
                        if (group.Value.Connections.TryDequeue(out connection))
                        {
                            connection.Dispose();
                        }
                    }
                }

                _thread.Dispose();
            }
        }

        public class Pool
        {
            public SemaphoreSlim Concurrency { get; }

            public ConcurrentQueue<HttpClientConnection> Connections { get; } = new ConcurrentQueue<HttpClientConnection>();

            public Pool(int maxConcurrency)
            {
                Concurrency = new SemaphoreSlim(maxConcurrency);
            }
        }
    }
}
