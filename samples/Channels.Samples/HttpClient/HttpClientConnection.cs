using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading.Tasks;
using Channels.Networking.Libuv;

namespace Channels.Samples
{
    public class HttpClientConnection
    {
        private static Vector<byte> _vectorCRs = new Vector<byte>((byte)'\r');
        private static Vector<byte> _vectorLFs = new Vector<byte>((byte)'\n');
        private static Vector<byte> _vectorSpaces = new Vector<byte>((byte)' ');
        private static Vector<byte> _vectorColons = new Vector<byte>((byte)':');

        private readonly UvTcpClientConnection _connection;

        private int _previousContentLength;
        private ReadIterator _consumed;

        internal ConnectionPool.Pool Pool { get; set; }

        public HttpClientConnection(UvTcpClientConnection connection)
        {
            _connection = connection;
        }

        public async Task<HttpResponseMessage> ExecuteRequestAsync(HttpRequestMessage request)
        {
            var path = request.RequestUri.GetComponents(UriComponents.PathAndQuery, UriFormat.SafeUnescaped);

            var connection = _connection;

            var requestBuffer = connection.Input.Alloc();
            WritableBufferExtensions.WriteAsciiString(ref requestBuffer, $"{request.Method} {path} HTTP/1.1");
            WriteHeaders(request.Headers, ref requestBuffer);

            // End of the headers
            WritableBufferExtensions.WriteAsciiString(ref requestBuffer, "\r\n\r\n");

            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                WriteHeaders(request.Content.Headers, ref requestBuffer);

                await connection.Input.WriteAsync(requestBuffer);

                // Copy the body to the input channel
                var body = await request.Content.ReadAsStreamAsync();

                await body.CopyToAsync(connection.Input);
            }
            else
            {
                await connection.Input.WriteAsync(requestBuffer);
            }

            var response = await ProduceResponse();

            // Get off the libuv thread
            await Task.Yield();

            return response;
        }

        private async Task<HttpResponseMessage> ProduceResponse()
        {
            var connection = _connection;

            var response = new HttpResponseMessage();
            response.Content = new ChannelHttpContent(connection.Output);

            // TODO: pipelining support!
            while (true)
            {
                await connection.Output;

                var responseBuffer = connection.Output.BeginRead();
                var consumed = responseBuffer.Start;

                var needMoreData = true;

                try
                {
                    if (consumed.Equals(_consumed))
                    {
                        var oldBody = responseBuffer.Slice(0, _previousContentLength);

                        if (oldBody.Length != _previousContentLength)
                        {
                            // Not enough data
                            continue;
                        }

                        // The caller didn't read the body
                        responseBuffer = responseBuffer.Slice(_previousContentLength);
                        consumed = responseBuffer.Start;

                        _consumed = default(ReadIterator);
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
                        // Bad response
                        throw new InvalidOperationException();
                    }

                    response.StatusCode = (HttpStatusCode)responseLine.Slice(0, delim).ReadUInt32();
                    responseLine = responseLine.Slice(delim).Slice(1);

                    delim = responseLine.IndexOf(ref _vectorSpaces);

                    if (delim.IsEnd)
                    {
                        // Bad response
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
                            // Bad response
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
                    connection.Output.EndRead(consumed);
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

                _consumed = consumed;

                checked
                {
                    // BAD but it's a proof of concept ok?
                    _previousContentLength = (int)length.Value;
                    ((ChannelHttpContent)response.Content).ContentLength = (int)length;
                }

                break;
            }

            return response;
        }

        private static void WriteHeaders(HttpHeaders headers, ref WritableBuffer buffer)
        {
            foreach (var header in headers)
            {
                WritableBufferExtensions.WriteAsciiString(ref buffer, $"{header.Key}:{string.Join(",", header.Value)}\r\n");
            }
        }

        public void Dispose()
        {
            _connection.Input.CompleteWriting();
        }
    }
}
