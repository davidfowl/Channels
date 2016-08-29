using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Channels.Samples
{
    public class LibuvHttpClientHandler : HttpClientHandler
    {
        private ConnectionPool _connectionPool = new ConnectionPool();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpClientConnection connection = null;
            HttpResponseMessage response = null;

            try
            {
                connection = await _connectionPool.GetConnectionAsync(request);

                response = await connection.ExecuteRequestAsync(request);
            }
            finally
            {
                if (connection != null)
                {
                    _connectionPool.Return(connection);
                }
            }

            return response;
        }

        protected override void Dispose(bool disposing)
        {
            _connectionPool.Dispose();

            base.Dispose(disposing);
        }
    }
}
