using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Channels.Text.Primitives;
using Microsoft.AspNetCore.Hosting.Server;

namespace Channels.Samples.Http
{
    public partial class HttpConnection<TContext>
    {
        private static readonly byte[] _http11Bytes = Encoding.UTF8.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _chunkedEndBytes = Encoding.UTF8.GetBytes("0\r\n\r\n");
        private static readonly byte[] _endChunkBytes = Encoding.ASCII.GetBytes("\r\n");

        private readonly IReadableChannel _input;
        private readonly IWritableChannel _output;
        private readonly IHttpApplication<TContext> _application;

        public RequestHeaderDictionary RequestHeaders => _parser.RequestHeaders;
        public ResponseHeaderDictionary ResponseHeaders { get; } = new ResponseHeaderDictionary();

        public ReadableBuffer HttpVersion => _parser.HttpVersion;
        public ReadableBuffer Path => _parser.Path;
        public ReadableBuffer Method => _parser.Method;

        // TODO: Check the http version
        public bool KeepAlive => true; //RequestHeaders.ContainsKey("Connection") && string.Equals(RequestHeaders["Connection"], "keep-alive");

        private bool HasContentLength => ResponseHeaders.ContainsKey("Content-Length");
        private bool HasTransferEncoding => ResponseHeaders.ContainsKey("Transfer-Encoding");

        private HttpRequestStream<TContext> _requestBody;
        private HttpResponseStream<TContext> _responseBody;

        private bool _autoChunk;

        private readonly HttpRequestParser _parser = new HttpRequestParser();
        private readonly ChannelFactory _factory;

        public HttpConnection(ChannelFactory factory, IHttpApplication<TContext> application, IReadableChannel input, IWritableChannel output)
        {
            _factory = factory;
            _application = application;
            _input = input;
            _output = output;
            _requestBody = new HttpRequestStream<TContext>(this);
            _responseBody = new HttpResponseStream<TContext>(this);

            RequestBody = factory.CreateChannel();
            ResponseBody = factory.CreateChannel();
        }

        public Channel RequestBody { get; private set; }

        public Channel ResponseBody { get; private set; }

        public async Task ProcessAllRequests()
        {
            Reset();

            while (true)
            {
                var result = await _input.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (buffer.IsEmpty && result.IsCompleted)
                    {
                        // We're done with this connection
                        return;
                    }

                    var parserResult = _parser.ParseRequest(ref buffer);

                    switch (parserResult)
                    {
                        case HttpRequestParser.ParseResult.Incomplete:
                            if (result.IsCompleted)
                            {
                                // Didn't get the whole request and the connection ended
                                throw new EndOfStreamException();
                            }
                            // Need more data
                            continue;
                        case HttpRequestParser.ParseResult.Complete:
                            // Done
                            break;
                        case HttpRequestParser.ParseResult.BadRequest:
                            // TODO: Don't throw here;
                            throw new Exception();
                        default:
                            break;
                    }

                }
                catch (Exception)
                {
                    StatusCode = 400;

                    // Start processing the response body
                    Task task = ProcessResponseBody();

                    CompleteResponse();

                    await task;

                    return;
                }
                finally
                {
                    _input.Advance(buffer.Start, buffer.End);
                }

                Task requestBodyTask = Task.CompletedTask;

                // TODO: Handle other body types
                if (RequestHeaders.ContainsKey("Content-Length"))
                {
                    var contentLength = RequestHeaders.GetHeaderRaw("Content-Length").GetUInt32();

                    // Process the request body
                    requestBodyTask = ProcessRequestBody(contentLength);
                }

                // Start processing the response body
                Task responseBodyTask = ProcessResponseBody();

                var context = _application.CreateContext(this);

                try
                {
                    await _application.ProcessRequestAsync(context);
                }
                catch (Exception ex)
                {
                    StatusCode = 500;

                    _application.DisposeContext(context, ex);
                }
                finally
                {
                    CompleteResponse();

                    // Wait for the body tasks to drain so we don't leak
                    await Task.WhenAll(requestBodyTask, responseBodyTask);
                }

                if (!KeepAlive)
                {
                    break;
                }

                Reset();
            }
        }

        private void CompleteResponse()
        {
            // The http request is done

            // The application is done producing the response body
            ResponseBody.CompleteWriter();

            // The application is done consuming the request body
            RequestBody.CompleteReader();
        }

        private async Task ProcessRequestBody(uint contentLength)
        {
            // Wait until someboy starts reading the body
            await RequestBody.ReadingStarted;

            int remaining = (int)contentLength;

            // Read up to the content length
            while (remaining > 0)
            {
                var result = await _input.ReadAsync();
                var inputBuffer = result.Buffer;

                var fin = result.IsCompleted;

                var consumed = inputBuffer.Start;

                try
                {
                    if (inputBuffer.IsEmpty && fin)
                    {
                        return;
                    }

                    var data = inputBuffer.Slice(0, remaining);

                    // Flow the buffers to the request body channel up to content length
                    var output = RequestBody.Alloc();
                    output.Append(data);
                    await output.FlushAsync();

                    consumed = data.End;
                    remaining -= data.Length;
                }
                finally
                {
                    _input.Advance(consumed);
                }
            }

            RequestBody.CompleteWriter();
        }

        private async Task ProcessResponseBody()
        {
            while (true)
            {
                var result = await ResponseBody.ReadAsync();
                var inputBuffer = result.Buffer;

                var outputBuffer = _output.Alloc();
                WriteBeginResponseHeaders(outputBuffer);

                try
                {
                    if (inputBuffer.IsEmpty && result.IsCompleted)
                    {
                        break;
                    }

                    if (_autoChunk)
                    {
                        outputBuffer.WriteHex(inputBuffer.Length);
                        outputBuffer.Write(_endChunkBytes);
                        outputBuffer.Append(inputBuffer);
                        outputBuffer.Write(_endChunkBytes);
                    }
                    else
                    {
                        outputBuffer.Append(inputBuffer);
                    }
                }
                finally
                {
                    await outputBuffer.FlushAsync();

                    ResponseBody.Advance(inputBuffer.End);
                }
            }

            if (_autoChunk)
            {
                var buffer = _output.Alloc();
                WriteEndResponse(buffer);
                await buffer.FlushAsync();
            }

            ResponseBody.CompleteReader();
        }

        private void Reset()
        {
            RequestBody.Reset();
            ResponseBody.Reset();

            _parser.Reset();
            ResponseHeaders.Reset();
            HasStarted = false;
            StatusCode = 200;
            _autoChunk = false;
            _method = null;
            _path = null;
        }

        private void WriteBeginResponseHeaders(WritableBuffer buffer)
        {
            if (HasStarted)
            {
                return;
            }

            HasStarted = true;

            buffer.Write(_http11Bytes);
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            buffer.Write(status);

            _autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            ResponseHeaders.CopyTo(_autoChunk, buffer);
        }

        private void WriteEndResponse(WritableBuffer buffer)
        {
            buffer.Write(_chunkedEndBytes);
        }
    }
}
