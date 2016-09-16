using System;
using System.Numerics;
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

        private readonly IReadableChannel _input;
        private readonly IWritableChannel _output;
        private readonly IHttpApplication<TContext> _application;

        public RequestHeaderDictionary RequestHeaders => _parser.RequestHeaders;
        public ResponseHeaderDictionary ResponseHeaders { get; } = new ResponseHeaderDictionary();

        public ReadableBuffer HttpVersion => _parser.HttpVersion;
        public ReadableBuffer Path => _parser.Path;
        public ReadableBuffer Method => _parser.Method;

        public IReadableChannel Input => _input;

        public IWritableChannel Output => _output;

        // TODO: Check the http version
        public bool KeepAlive => true; //RequestHeaders.ContainsKey("Connection") && string.Equals(RequestHeaders["Connection"], "keep-alive");

        private bool HasContentLength => ResponseHeaders.ContainsKey("Content-Length");
        private bool HasTransferEncoding => ResponseHeaders.ContainsKey("Transfer-Encoding");

        private HttpBodyStream<TContext> _initialBody;

        private bool _autoChunk;

        private HttpRequestParser _parser = new HttpRequestParser();

        public HttpConnection(IHttpApplication<TContext> application, IReadableChannel input, IWritableChannel output)
        {
            _application = application;
            _input = input;
            _output = output;
            _initialBody = new HttpBodyStream<TContext>(this);
        }

        public async Task ProcessAllRequests()
        {
            Reset();

            while (true)
            {
                var buffer = await _input.ReadAsync();
                var consumed = buffer.Start;

                try
                {
                    if (buffer.IsEmpty && _input.Completion.IsCompleted)
                    {
                        // We're done with this connection
                        return;
                    }

                    var result = _parser.ParseRequest(ref buffer);

                    // Update consumed
                    consumed = buffer.Start;

                    switch (result)
                    {
                        case HttpRequestParser.ParseResult.Incomplete:
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

                    await EndResponse();

                    return;
                }
                finally
                {
                    buffer.Consumed(consumed);
                }

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
                    await EndResponse();
                }

                if (!KeepAlive)
                {
                    break;
                }

                Reset();
            }
        }

        private Task EndResponse()
        {
            var buffer = default(WritableBuffer);
            var hasBuffer = false;

            if (!HasStarted)
            {
                buffer = _output.Alloc();

                WriteBeginResponseHeaders(ref buffer, ref _autoChunk);

                hasBuffer = true;
            }

            if (_autoChunk)
            {
                if (!hasBuffer)
                {
                    buffer = _output.Alloc();
                }

                WriteEndResponse(ref buffer);

                return buffer.FlushAsync();
            }

            return Task.CompletedTask;
        }

        private void Reset()
        {
            Body = _initialBody;
            _parser.Reset();
            ResponseHeaders.Reset();
            HasStarted = false;
            StatusCode = 200;
            _autoChunk = false;
            _method = null;
            _path = null;
        }

        public Task WriteAsync(Span<byte> data)
        {
            var buffer = _output.Alloc();

            if (!HasStarted)
            {
                WriteBeginResponseHeaders(ref buffer, ref _autoChunk);
            }

            if (_autoChunk)
            {
                ChunkWriter.WriteBeginChunkBytes(ref buffer, data.Length);
                buffer.Write(data);
                ChunkWriter.WriteEndChunkBytes(ref buffer);
            }
            else
            {
                buffer.Write(data);
            }

            return buffer.FlushAsync();
        }

        private void WriteBeginResponseHeaders(ref WritableBuffer buffer, ref bool autoChunk)
        {
            if (HasStarted)
            {
                return;
            }

            HasStarted = true;

            buffer.Write(_http11Bytes);
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            buffer.Write(status);

            autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            ResponseHeaders.CopyTo(autoChunk, ref buffer);
        }

        private void WriteEndResponse(ref WritableBuffer buffer)
        {
            buffer.Write(_chunkedEndBytes);
        }
    }
}
