using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;

namespace Channels.Samples.Http
{
    public partial class HttpConnection<TContext>
    {
        private static readonly byte[] _http11Bytes = Encoding.UTF8.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _chunkedEndBytes = Encoding.UTF8.GetBytes("0\r\n\r\n");

        private static Vector<byte> _vectorCRs = new Vector<byte>((byte)'\r');
        private static Vector<byte> _vectorLFs = new Vector<byte>((byte)'\n');
        private static Vector<byte> _vectorColons = new Vector<byte>((byte)':');
        private static Vector<byte> _vectorSpaces = new Vector<byte>((byte)' ');
        private static Vector<byte> _vectorTabs = new Vector<byte>((byte)'\t');
        private static Vector<byte> _vectorQuestionMarks = new Vector<byte>((byte)'?');
        private static Vector<byte> _vectorPercentages = new Vector<byte>((byte)'%');

        private readonly IReadableChannel _input;
        private readonly IWritableChannel _output;
        private readonly IHttpApplication<TContext> _application;

        public RequestHeaderDictionary RequestHeaders { get; } = new RequestHeaderDictionary();
        public ResponseHeaderDictionary ResponseHeaders { get; } = new ResponseHeaderDictionary();

        public IReadableChannel RequestBody => _input;

        public ReadableBuffer HttpVersion { get; set; }
        public ReadableBuffer Path { get; set; }
        public ReadableBuffer Method { get; set; }

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
                await _input;

                var buffer = _input.Read();
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
                        var delim = buffer.IndexOf(ref _vectorLFs);
                        if (delim.IsEnd)
                        {
                            continue;
                        }

                        // Grab the entire start line
                        var startLine = buffer.Slice(0, delim);

                        // Move the buffer to the rest
                        buffer = buffer.Slice(delim).Slice(1);

                        delim = startLine.IndexOf(ref _vectorSpaces);
                        if (delim.IsEnd)
                        {
                            throw new Exception();
                        }

                        var method = startLine.Slice(0, delim);
                        Method = method.Clone();

                        // Skip ' '
                        startLine = startLine.Slice(delim).Slice(1);

                        delim = startLine.IndexOf(ref _vectorSpaces);
                        if (delim.IsEnd)
                        {
                            throw new Exception();
                        }

                        var path = startLine.Slice(0, delim);
                        Path = path.Clone();

                        // Skip ' '
                        startLine = startLine.Slice(delim).Slice(1);

                        delim = startLine.IndexOf(ref _vectorCRs);
                        if (delim.IsEnd)
                        {
                            throw new Exception();
                        }

                        var httpVersion = startLine.Slice(0, delim);
                        HttpVersion = httpVersion.Clone();

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
                        var delim = buffer.IndexOf(ref _vectorLFs);
                        if (delim.IsEnd)
                        {
                            break;
                        }

                        var headerPair = buffer.Slice(0, delim);
                        buffer = buffer.Slice(delim).Slice(1);

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

                return buffer.CommitAsync();
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

            HttpVersion.Dispose();
            Method.Dispose();
            Path.Dispose();

        }

        public Task WriteAsync(byte[] array, int offset, int count)
        {
            var buffer = _output.Alloc();

            if (!HasStarted)
            {
                WriteBeginResponseHeaders(ref buffer, ref _autoChunk);
            }

            if (_autoChunk)
            {
                ChunkWriter.WriteBeginChunkBytes(ref buffer, count);
                buffer.Write(array, offset, count);
                ChunkWriter.WriteEndChunkBytes(ref buffer);
            }
            else
            {
                buffer.Write(array, offset, count);
            }

            return buffer.CommitAsync();
        }

        private void WriteBeginResponseHeaders(ref WritableBuffer buffer, ref bool autoChunk)
        {
            if (HasStarted)
            {
                return;
            }

            HasStarted = true;

            buffer.Write(_http11Bytes, 0, _http11Bytes.Length);
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            buffer.Write(status, 0, status.Length);

            autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            ResponseHeaders.CopyTo(autoChunk, ref buffer);
        }

        private void WriteEndResponse(ref WritableBuffer buffer)
        {
            buffer.Write(_chunkedEndBytes, 0, _chunkedEndBytes.Length);
        }

        private enum ParsingState
        {
            StartLine,
            Headers
        }
    }
}
