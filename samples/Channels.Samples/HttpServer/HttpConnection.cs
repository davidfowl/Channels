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

        public RequestHeaderDictionary RequestHeaders { get; } = new RequestHeaderDictionary();
        public ResponseHeaderDictionary ResponseHeaders { get; } = new ResponseHeaderDictionary();

        public ReadableBuffer HttpVersion { get; set; }
        public ReadableBuffer Path { get; set; }
        public ReadableBuffer Method { get; set; }

        public IReadableChannel Input => _input;

        public IWritableChannel Output => _output;

        // TODO: Check the http version
        public bool KeepAlive => true; //RequestHeaders.ContainsKey("Connection") && string.Equals(RequestHeaders["Connection"], "keep-alive");

        private bool HasContentLength => ResponseHeaders.ContainsKey("Content-Length");
        private bool HasTransferEncoding => ResponseHeaders.ContainsKey("Transfer-Encoding");

        private HttpBodyStream<TContext> _initialBody;

        private bool _autoChunk;

        private ParsingState _state;

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
                bool needMoreData = true;

                try
                {
                    if (buffer.IsEmpty && _input.Completion.IsCompleted)
                    {
                        // We're done with this connection
                        return;
                    }

                    if (_state == ParsingState.StartLine)
                    {
                        // Find \n
                        var delim = buffer.IndexOf(ref CommonVectors.LF);
                        if (delim == ReadCursor.NotFound)
                        {
                            continue;
                        }

                        // Grab the entire start line
                        var startLine = buffer.Slice(0, delim);

                        // Move the buffer to the rest
                        buffer = buffer.Slice(delim).Slice(1);

                        delim = startLine.IndexOf(ref CommonVectors.Space);
                        if (delim == ReadCursor.NotFound)
                        {
                            throw new Exception();
                        }

                        var method = startLine.Slice(0, delim);
                        Method = method.Preserve();

                        // Skip ' '
                        startLine = startLine.Slice(delim).Slice(1);

                        delim = startLine.IndexOf(ref CommonVectors.Space);
                        if (delim == ReadCursor.NotFound)
                        {
                            throw new Exception();
                        }

                        var path = startLine.Slice(0, delim);
                        Path = path.Preserve();

                        // Skip ' '
                        startLine = startLine.Slice(delim).Slice(1);

                        delim = startLine.IndexOf(ref CommonVectors.CR);
                        if (delim == ReadCursor.NotFound)
                        {
                            throw new Exception();
                        }

                        var httpVersion = startLine.Slice(0, delim);
                        HttpVersion = httpVersion.Preserve();

                        _state = ParsingState.Headers;
                        consumed = startLine.End;
                    }

                    // Parse headers
                    // key: value\r\n

                    while (!buffer.IsEmpty)
                    {
                        var ch = buffer.Peek();

                        if (ch == -1)
                        {
                            break;
                        }

                        if (ch == '\r')
                        {
                            // Check for final CRLF.
                            buffer = buffer.Slice(1);
                            ch = buffer.Peek();
                            buffer = buffer.Slice(1);

                            if (ch == -1)
                            {
                                break;
                            }
                            else if (ch == '\n')
                            {
                                consumed = buffer.Start;
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
                        var delim = buffer.IndexOf(ref CommonVectors.LF);
                        if (delim == ReadCursor.NotFound)
                        {
                            break;
                        }

                        var headerPair = buffer.Slice(0, delim);
                        buffer = buffer.Slice(delim).Slice(1);

                        // :
                        delim = headerPair.IndexOf(ref CommonVectors.Colon);
                        if (delim == ReadCursor.NotFound)
                        {
                            throw new Exception();
                        }

                        headerName = headerPair.Slice(0, delim).TrimStart();
                        headerPair = headerPair.Slice(delim).Slice(1);

                        // \r
                        delim = headerPair.IndexOf(ref CommonVectors.CR);
                        if (delim == ReadCursor.NotFound)
                        {
                            // Bad request
                            throw new Exception();
                        }

                        headerValue = headerPair.Slice(0, delim).TrimStart();
                        RequestHeaders.SetHeader(ref headerName, ref headerValue);

                        // Move the consumed
                        consumed = buffer.Start;
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

                if (needMoreData)
                {
                    continue;
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
            RequestHeaders.Reset();
            ResponseHeaders.Reset();
            HasStarted = false;
            StatusCode = 200;
            _autoChunk = false;
            _state = ParsingState.StartLine;
            _method = null;
            _path = null;

            HttpVersion.Dispose();
            Method.Dispose();
            Path.Dispose();

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

            buffer.Write(_http11Bytes.Slice());
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            buffer.Write(status.Slice());

            autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            ResponseHeaders.CopyTo(autoChunk, ref buffer);
        }

        private void WriteEndResponse(ref WritableBuffer buffer)
        {
            buffer.Write(_chunkedEndBytes.Slice());
        }

        private enum ParsingState
        {
            StartLine,
            Headers
        }
    }
}
