using System;
using System.Numerics;
using System.Text;
using System.Text.Formatting;
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
        private readonly WritableChannelFormatter _outputFormatter;

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
            _outputFormatter = _output.GetFormatter(EncodingData.InvariantUtf8);
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
            if (!HasStarted)
            {
                WriteBeginResponseHeaders();
            }

            if (_autoChunk)
            {
                WriteEndResponse();

                return _outputFormatter.FlushAsync();
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
            if (!HasStarted)
            {
                WriteBeginResponseHeaders();
            }

            if (_autoChunk)
            {
                _outputFormatter.Append(data.Length, Format.Parsed.HexLowercase);
                _outputFormatter.Write(data);
                _outputFormatter.Write(_endChunkBytes);
            }
            else
            {
                _outputFormatter.Write(data);
            }

            return _outputFormatter.FlushAsync();
        }

        private void WriteBeginResponseHeaders()
        {
            if (HasStarted)
            {
                return;
            }

            HasStarted = true;

            _outputFormatter.Write(_http11Bytes);
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            _outputFormatter.Write(status);

            _autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            ResponseHeaders.CopyTo(_autoChunk, _outputFormatter);
        }

        private void WriteEndResponse()
        {
            _outputFormatter.Write(_chunkedEndBytes);
        }
    }
}
