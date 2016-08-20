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
        private readonly ChannelFactory _channelFactory;
        private IHttpApplication<TContext> _application;
        private MemoryPoolChannel _responseBody;
        private Task _responseBodyProcessing;

        public HeaderDictionary RequestHeaders { get; } = new HeaderDictionary();
        public HeaderDictionary ResponseHeaders { get; } = new HeaderDictionary();

        public IReadableChannel RequestBody => _input;
        public IWritableChannel ResponseBody => _responseBody;

        private string HttpVersion { get; set; }

        // TODO: Check the http version
        public bool KeepAlive => true; //RequestHeaders.ContainsKey("Connection") && string.Equals(RequestHeaders["Connection"], "keep-alive");

        private bool HasContentLength => ResponseHeaders.ContainsKey("Content-Length");
        private bool HasTransferEncoding => ResponseHeaders.ContainsKey("Transfer-Encoding");

        private HttpBodyStream<TContext> _initialBody;

        public HttpConnection(IHttpApplication<TContext> application, IReadableChannel input, IWritableChannel output, ChannelFactory channelFactory)
        {
            _application = application;
            _input = input;
            _output = output;
            _channelFactory = channelFactory;
            _initialBody = new HttpBodyStream<TContext>(this);
        }

        public async Task ProcessRequest()
        {
            Reset();

            while (true)
            {
                await _input;

                var begin = _input.BeginRead();
                var end = begin;

                bool needMoreData = true;

                if (begin.IsEnd && _input.Completion.IsCompleted)
                {
                    // We're done with this connection
                    return;
                }

                try
                {

                    if (end.Seek(ref _vectorSpaces) == -1)
                    {
                        continue;
                    }

                    // Assume it'll be in a single block
                    BufferSpan span;
                    if (begin.TryGetBuffer(end, out span))
                    {
                        Method = Encoding.UTF8.GetString(span.Buffer.Array, span.Buffer.Offset, span.Length);
                    }

                    // Skip ' '
                    begin.Take();
                    end.Take();

                    if (end.Seek(ref _vectorSpaces) == -1)
                    {
                        continue;
                    }

                    if (begin.TryGetBuffer(end, out span))
                    {
                        Path = Encoding.UTF8.GetString(span.Buffer.Array, span.Buffer.Offset, span.Length);
                    }

                    begin.Take();
                    end.Take();

                    if (end.Seek(ref _vectorLFs) == -1)
                    {
                        continue;
                    }

                    if (begin.TryGetBuffer(end, out span))
                    {
                        HttpVersion = Encoding.UTF8.GetString(span.Buffer.Array, span.Buffer.Offset, span.Length).Trim();
                    }
                    // Skip '\n'
                    begin.Take();
                    end.Take();

                    // Parse headers
                    // key: value\r\n

                    while (!end.IsEnd)
                    {
                        var ch = end.Peek();

                        if (ch == -1)
                        {
                            break;
                        }

                        if (ch == '\r')
                        {
                            // Check for final CRLF.
                            end.Take();
                            ch = end.Take();

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

                        string headerName = null;
                        string headerValue = null;

                        // :
                        if (end.Seek(ref _vectorColons) == -1)
                        {
                            break;
                        }

                        if (begin.TryGetBuffer(end, out span))
                        {
                            headerName = Encoding.UTF8.GetString(span.Buffer.Array, span.Buffer.Offset, span.Length);
                        }

                        begin.Take();
                        end.Take();

                        // \n
                        if (end.Seek(ref _vectorLFs) == -1)
                        {
                            break;
                        }

                        if (begin.TryGetBuffer(end, out span))
                        {
                            headerValue = Encoding.UTF8.GetString(span.Buffer.Array, span.Buffer.Offset, span.Length).Trim();
                        }

                        RequestHeaders[headerName] = headerValue;
                        begin.Take();
                        end.Take();
                    }
                }
                finally
                {
                    _input.EndRead(end);
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
                _responseBody.CompleteWriting();
            }
            catch (Exception ex)
            {
                StatusCode = 500;

                _responseBody.CompleteWriting(ex);
                _application.DisposeContext(context, ex);
            }
            finally
            {
                await _responseBodyProcessing;
            }
        }

        private void Reset()
        {
            _responseBody = _channelFactory.CreateChannel();
            Body = _initialBody;
            RequestHeaders.Clear();
            ResponseHeaders.Clear();
            HasStarted = false;
            StatusCode = 200;
            _responseBodyProcessing = ProcessResponse();
        }

        private async Task ProcessResponse()
        {
            var buffer = _output.Alloc();
            var autoChunk = false;

            while (true)
            {
                await _responseBody;

                var begin = _responseBody.BeginRead();
                var end = begin;

                if (begin.IsEnd && _responseBody.Completion.IsCompleted)
                {
                    break;
                }

                WriteBeginResponseHeaders(ref buffer, ref autoChunk);

                try
                {
                    BufferSpan span;
                    while (end.TryGetBuffer(out span))
                    {
                        if (autoChunk)
                        {
                            ChunkWriter.WriteBeginChunkBytes(ref buffer, span.Length);
                            buffer.Append(begin, end);
                            ChunkWriter.WriteEndChunkBytes(ref buffer);
                        }
                        else
                        {
                            buffer.Append(begin, end);
                        }

                        begin = end;
                    }
                }
                finally
                {
                    _responseBody.EndRead(end);
                }
            }

            WriteBeginResponseHeaders(ref buffer, ref autoChunk);

            if (autoChunk)
            {
                WriteEndResponse(ref buffer);
            }

            await _output.WriteAsync(buffer);

            _responseBody.CompleteReading();
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
