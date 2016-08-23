using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;

namespace Channels.Samples.Http
{
    public partial class HttpConnection<TContext>
    {
        private static Vector<byte> _vectorCRs = new Vector<byte>((byte)'\r');
        private static Vector<byte> _vectorLFs = new Vector<byte>((byte)'\n');
        private static Vector<byte> _vectorColons = new Vector<byte>((byte)':');
        private static Vector<byte> _vectorSpaces = new Vector<byte>((byte)' ');
        private static Vector<byte> _vectorTabs = new Vector<byte>((byte)'\t');
        private static Vector<byte> _vectorQuestionMarks = new Vector<byte>((byte)'?');
        private static Vector<byte> _vectorPercentages = new Vector<byte>((byte)'%');

        private readonly IReadableChannel _input;
        private readonly IWritableChannel _output;
        private IHttpApplication<TContext> _application;

        public HeaderDictionary RequestHeaders { get; } = new HeaderDictionary();
        public HeaderDictionary ResponseHeaders { get; } = new HeaderDictionary();

        public IReadableChannel RequestBody => _input;

        private string HttpVersion { get; set; }

        // TODO: Check the http version
        public bool KeepAlive => true; //RequestHeaders.ContainsKey("Connection") && string.Equals(RequestHeaders["Connection"], "keep-alive");

        private bool HasContentLength => ResponseHeaders.ContainsKey("Content-Length");
        private bool HasTransferEncoding => ResponseHeaders.ContainsKey("Transfer-Encoding");

        private HttpBodyStream<TContext> _initialBody;

        private bool _autoChunk;

        public HttpConnection(IHttpApplication<TContext> application, IReadableChannel input, IWritableChannel output)
        {
            _application = application;
            _input = input;
            _output = output;
            _initialBody = new HttpBodyStream<TContext>(this);
        }

        public async Task ProcessRequest()
        {
            Reset();

            while (true)
            {
                await _input;

                var buffer = _input.BeginRead();

                bool needMoreData = true;

                if (buffer.Length == 0 && _input.Completion.IsCompleted)
                {
                    // We're done with this connection
                    return;
                }

                try
                {
                    var delim = buffer.Seek(ref _vectorSpaces);
                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var method = buffer.Slice(0, delim);
                    Method = method.GetUtf8String();

                    // Skip ' '
                    buffer = buffer.Slice(delim).Slice(1);

                    delim = buffer.Seek(ref _vectorSpaces);
                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var path = buffer.Slice(0, delim);
                    Path = path.GetUtf8String();

                    // Skip ' '
                    buffer = buffer.Slice(delim).Slice(1);

                    delim = buffer.Seek(ref _vectorLFs);
                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var httpVersion = buffer.Slice(0, delim);
                    HttpVersion = httpVersion.GetUtf8String().Trim();

                    buffer = buffer.Slice(delim).Slice(1);

                    // Parse headers
                    // key: value\r\n

                    while (buffer.Length > 0)
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
                                needMoreData = false;
                                break;
                            }

                            // Headers don't end in CRLF line.
                        }

                        var headerName = default(ReadableBuffer);
                        var headerValue = default(ReadableBuffer);

                        // :
                        delim = buffer.Seek(ref _vectorColons);
                        if (delim.IsEnd)
                        {
                            break;
                        }

                        headerName = buffer.Slice(0, delim);
                        buffer = buffer.Slice(delim).Slice(1);

                        // \n
                        delim = buffer.Seek(ref _vectorLFs);
                        if (delim.IsEnd)
                        {
                            break;
                        }

                        headerValue = buffer.Slice(0, delim);
                        buffer = buffer.Slice(delim).Slice(1);

                        RequestHeaders[headerName.GetUtf8String().Trim()] = headerValue.GetUtf8String().Trim();
                    }
                }
                finally
                {
                    _input.EndRead(buffer);
                }

                if (!needMoreData)
                {
                    break;
                }
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
        }

        private Task EndResponse()
        {
            if (_autoChunk)
            {
                var buffer = _output.Alloc();
                WriteEndResponse(ref buffer);
                return _output.WriteAsync(buffer);
            }

            return Task.CompletedTask;
        }

        private void Reset()
        {
            Body = _initialBody;
            RequestHeaders.Clear();
            ResponseHeaders.Clear();
            HasStarted = false;
            StatusCode = 200;
            _autoChunk = false;
        }

        public Task WriteAsync(ArraySegment<byte> data)
        {
            var buffer = _output.Alloc();

            if (!HasStarted)
            {
                WriteBeginResponseHeaders(ref buffer, ref _autoChunk);
            }

            if (_autoChunk)
            {
                ChunkWriter.WriteBeginChunkBytes(ref buffer, data.Count);
                buffer.Write(data.Array, data.Offset, data.Count);
                ChunkWriter.WriteEndChunkBytes(ref buffer);
            }
            else
            {
                buffer.Write(data.Array, data.Offset, data.Count);
            }

            return _output.WriteAsync(buffer);
        }

        private void WriteBeginResponseHeaders(ref WritableBuffer buffer, ref bool autoChunk)
        {
            if (HasStarted)
            {
                return;
            }

            HasStarted = true;

            ResponseHeaders["Server"] = "Channels HTTP Sample Server";
            ResponseHeaders["Date"] = DateTime.UtcNow.ToString("r");

            autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            if (autoChunk)
            {
                ResponseHeaders["Transfer-Encoding"] = "chunked";
            }

            var httpVersion = Encoding.UTF8.GetBytes("HTTP/1.1 ");
            buffer.Write(httpVersion, 0, httpVersion.Length);
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            buffer.Write(status, 0, status.Length);

            foreach (var header in ResponseHeaders)
            {
                var headerRaw = "\r\n" + header.Key + ": " + header.Value;
                var headerBytes = Encoding.UTF8.GetBytes(headerRaw);
                buffer.Write(headerBytes, 0, headerBytes.Length);
            }

            var crlf = Encoding.UTF8.GetBytes("\r\n\r\n");
            buffer.Write(crlf, 0, crlf.Length);
        }

        private void WriteEndResponse(ref WritableBuffer buffer)
        {
            var chunkedEnding = Encoding.UTF8.GetBytes("0\r\n\r\n");
            buffer.Write(chunkedEnding, 0, chunkedEnding.Length);
        }
    }
}
