using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Channels.Networking.Libuv;

namespace Channels.Samples
{
    public class LibuvHttpClientHandler : HttpClientHandler
    {
        private static Vector<byte> _vectorCRs = new Vector<byte>((byte)'\r');
        private static Vector<byte> _vectorLFs = new Vector<byte>((byte)'\n');
        private static Vector<byte> _vectorSpaces = new Vector<byte>((byte)' ');
        private static Vector<byte> _vectorColons = new Vector<byte>((byte)':');

        private readonly UvThread _thread = new UvThread();

        private ConcurrentDictionary<string, ConnectionState> _connectionPool = new ConcurrentDictionary<string, ConnectionState>();

        public LibuvHttpClientHandler()
        {

        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri.GetComponents(UriComponents.HostAndPort, UriFormat.SafeUnescaped);
            var path = request.RequestUri.GetComponents(UriComponents.PathAndQuery, UriFormat.SafeUnescaped);

            var state = _connectionPool.GetOrAdd(key, k => GetConnection(request));
            var connection = await state.ConnectionTask;

            var requestBuffer = connection.Input.Alloc();
            WritableBufferExtensions.WriteAsciiString(ref requestBuffer, $"{request.Method} {path} HTTP/1.1");
            WriteHeaders(request.Headers, ref requestBuffer);

            // End of the headers
            WritableBufferExtensions.WriteAsciiString(ref requestBuffer, "\r\n\r\n");

            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                WriteHeaders(request.Content.Headers, ref requestBuffer);

                await requestBuffer.CommitAsync();

                // Copy the body to the input channel
                var body = await request.Content.ReadAsStreamAsync();

                await body.CopyToAsync(connection.Input);
            }
            else
            {
                await requestBuffer.CommitAsync();
            }

            var response = new HttpResponseMessage();
            response.Content = new ChannelHttpContent(connection.Output);

            await ProduceResponse(state, connection, response);

            // Get off the libuv thread
            await Task.Yield();

            return response;
        }

        private static async Task ProduceResponse(ConnectionState state, UvTcpClientConnection connection, HttpResponseMessage response)
        {
            // TODO: pipelining support!
            while (true)
            {
                await connection.Output;

                var responseBuffer = connection.Output.Read();
                var consumed = responseBuffer.Start;

                var needMoreData = true;

                try
                {
                    if (consumed.Equals(state.Consumed))
                    {
                        var oldBody = responseBuffer.Slice(0, state.PreviousContentLength);

                        if (oldBody.Length != state.PreviousContentLength)
                        {
                            // Not enough data
                            continue;
                        }

                        // The caller didn't read the body
                        responseBuffer = responseBuffer.Slice(state.PreviousContentLength);
                        consumed = responseBuffer.Start;

                        state.Consumed = default(ReadCursor);
                    }

                    if (responseBuffer.IsEmpty && connection.Output.Completion.IsCompleted)
                    {
                        break;
                    }

                    var delim = responseBuffer.IndexOf(ref _vectorLFs);

                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var responseLine = responseBuffer.Slice(0, delim);
                    responseBuffer = responseBuffer.Slice(delim).Slice(1);

                    delim = responseLine.IndexOf(ref _vectorSpaces);

                    if (delim.IsEnd)
                    {
                        // Bad request
                        throw new InvalidOperationException();
                    }

                    consumed = responseBuffer.Start;

                    var httpVersion = responseLine.Slice(0, delim);
                    responseLine = responseLine.Slice(delim).Slice(1);

                    delim = responseLine.IndexOf(ref _vectorSpaces);

                    if (delim.IsEnd)
                    {
                        // Bad request
                        throw new InvalidOperationException();
                    }

                    response.StatusCode = (HttpStatusCode)responseLine.Slice(0, delim).ReadUInt32();
                    responseLine = responseLine.Slice(delim).Slice(1);

                    delim = responseLine.IndexOf(ref _vectorSpaces);

                    if (delim.IsEnd)
                    {
                        // Bad request
                        throw new InvalidOperationException();
                    }

                    while (!responseBuffer.IsEmpty)
                    {
                        var ch = responseBuffer.Peek();

                        if (ch == -1)
                        {
                            break;
                        }

                        if (ch == '\r')
                        {
                            // Check for final CRLF.
                            responseBuffer = responseBuffer.Slice(1);
                            ch = responseBuffer.Peek();
                            responseBuffer = responseBuffer.Slice(1);

                            if (ch == -1)
                            {
                                break;
                            }
                            else if (ch == '\n')
                            {
                                consumed = responseBuffer.Start;
                                needMoreData = false;
                                break;
                            }

                            // Headers don't end in CRLF line.
                            throw new Exception();
                        }

                        var headerName = default(ReadableBuffer);
                        var headerValue = default(ReadableBuffer);

                        // End of the header
                        // \n
                        delim = responseBuffer.IndexOf(ref _vectorLFs);
                        if (delim.IsEnd)
                        {
                            break;
                        }

                        var headerPair = responseBuffer.Slice(0, delim);
                        responseBuffer = responseBuffer.Slice(delim).Slice(1);

                        // :
                        delim = headerPair.IndexOf(ref _vectorColons);
                        if (delim.IsEnd)
                        {
                            throw new Exception();
                        }

                        headerName = headerPair.Slice(0, delim).TrimStart();
                        headerPair = headerPair.Slice(delim).Slice(1);

                        // \r
                        delim = headerPair.IndexOf(ref _vectorCRs);
                        if (delim.IsEnd)
                        {
                            // Bad request
                            throw new Exception();
                        }

                        headerValue = headerPair.Slice(0, delim).TrimStart();
                        var hKey = headerName.GetAsciiString();
                        var hValue = headerValue.GetAsciiString();

                        if (!response.Content.Headers.TryAddWithoutValidation(hKey, hValue))
                        {
                            response.Headers.TryAddWithoutValidation(hKey, hValue);
                        }

                        // Move the consumed
                        consumed = responseBuffer.Start;
                    }
                }
                catch (Exception ex)
                {
                    // Close the connection
                    connection.Input.CompleteWriting(ex);
                    break;
                }
                finally
                {
                    responseBuffer.Consumed(consumed);
                }

                if (needMoreData)
                {
                    continue;
                }

                // Only handle content length for now
                var length = response.Content.Headers.ContentLength;

                if (!length.HasValue)
                {
                    throw new NotSupportedException();
                }

                checked
                {
                    // BAD but it's a proof of concept ok?
                    state.PreviousContentLength = (int)length.Value;
                    ((ChannelHttpContent)response.Content).ContentLength = (int)length;
                    state.Consumed = consumed;
                }

                break;
            }
        }

        private static void WriteHeaders(HttpHeaders headers, ref WritableBuffer buffer)
        {
            foreach (var header in headers)
            {
                WritableBufferExtensions.WriteAsciiString(ref buffer, $"{header.Key}:{string.Join(",", header.Value)}\r\n");
            }
        }

        private ConnectionState GetConnection(HttpRequestMessage request)
        {
            var state = new ConnectionState
            {
                ConnectionTask = ConnectAsync(request)
            };

            return state;
        }

        private async Task<UvTcpClientConnection> ConnectAsync(HttpRequestMessage request)
        {
            var addresses = await Dns.GetHostAddressesAsync(request.RequestUri.Host);
            var port = request.RequestUri.Port;

            var address = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            var connection = new UvTcpClient(_thread, new IPEndPoint(address, port));
            return await connection.ConnectAsync();
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var state in _connectionPool)
            {
                state.Value.ConnectionTask.GetAwaiter().GetResult().Input.CompleteWriting();
            }

            _thread.Dispose();

            base.Dispose(disposing);
        }

        private class ConnectionState
        {
            public Task<UvTcpClientConnection> ConnectionTask { get; set; }

            public int PreviousContentLength { get; set; }

            public ReadCursor Consumed { get; set; }
        }
    }
}
