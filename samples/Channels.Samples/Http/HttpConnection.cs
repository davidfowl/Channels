using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;

namespace Channels.Samples.Http
{
    public partial class HttpConnection<TContext>
    {
        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();
        private static readonly byte[] _serverHeaderBytes = Encoding.UTF8.GetBytes("\r\nServer: Channels");
        private static readonly byte[] _chunkedHeaderBytes = Encoding.UTF8.GetBytes("\r\nTransfer-Encoding: chunked");
        private static readonly byte[] _http11Bytes = Encoding.UTF8.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _headersStartBytes = Encoding.UTF8.GetBytes("\r\n");
        private static readonly byte[] _headersSeperatorBytes = Encoding.UTF8.GetBytes(": ");
        private static readonly byte[] _headersEndBytes = Encoding.UTF8.GetBytes("\r\n\r\n");
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

        public async Task ProcessAllRequests()
        {
            Reset();

            while (true)
            {
                await _input;

                var buffer = _input.BeginRead();

                bool needMoreData = true;

                if (buffer.IsEmpty && _input.Completion.IsCompleted)
                {
                    // We're done with this connection
                    return;
                }

                try
                {
                    var delim = buffer.IndexOf(ref _vectorSpaces);
                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var method = buffer.Slice(0, delim);
                    Method = method.GetUtf8String();

                    // Skip ' '
                    buffer = buffer.Slice(delim).Slice(1);

                    delim = buffer.IndexOf(ref _vectorSpaces);
                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var path = buffer.Slice(0, delim);
                    Path = path.GetUtf8String();

                    // Skip ' '
                    buffer = buffer.Slice(delim).Slice(1);

                    delim = buffer.IndexOf(ref _vectorLFs);
                    if (delim.IsEnd)
                    {
                        continue;
                    }

                    var httpVersion = buffer.Slice(0, delim).Trim();
                    HttpVersion = httpVersion.GetUtf8String();

                    buffer = buffer.Slice(delim).Slice(1);

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
                                needMoreData = false;
                                break;
                            }

                            // Headers don't end in CRLF line.
                        }

                        var headerName = default(ReadableBuffer);
                        var headerValue = default(ReadableBuffer);

                        // :
                        delim = buffer.IndexOf(ref _vectorColons);
                        if (delim.IsEnd)
                        {
                            break;
                        }

                        headerName = buffer.Slice(0, delim).Trim();
                        buffer = buffer.Slice(delim).Slice(1);

                        // \n
                        delim = buffer.IndexOf(ref _vectorLFs);
                        if (delim.IsEnd)
                        {
                            break;
                        }

                        // Skip \r
                        headerValue = buffer.Slice(0, delim).Trim();
                        buffer = buffer.Slice(delim).Slice(1);

                        RequestHeaders[headerName.GetUtf8String()] = headerValue.GetUtf8String();
                    }
                }
                finally
                {
                    _input.EndRead(buffer.Start);
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

            return _output.WriteAsync(buffer);
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

            foreach (var header in ResponseHeaders)
            {
                buffer.Write(_headersStartBytes, 0, _headersStartBytes.Length);
                var headerBytes = Encoding.UTF8.GetBytes(header.Key);
                buffer.Write(headerBytes, 0, headerBytes.Length);
                buffer.Write(_headersSeperatorBytes, 0, _headersSeperatorBytes.Length);
                headerBytes = Encoding.UTF8.GetBytes(header.Value);
                buffer.Write(headerBytes, 0, headerBytes.Length);
            }


            autoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            if (autoChunk)
            {
                buffer.Write(_chunkedHeaderBytes, 0, _chunkedHeaderBytes.Length);
            }

            buffer.Write(_serverHeaderBytes, 0, _serverHeaderBytes.Length);
            var date = _dateHeaderValueManager.GetDateHeaderValues().Bytes;
            buffer.Write(date, 0, date.Length);

            buffer.Write(_headersEndBytes, 0, _headersEndBytes.Length);
        }

        private void WriteEndResponse(ref WritableBuffer buffer)
        {
            buffer.Write(_chunkedEndBytes, 0, _chunkedEndBytes.Length);
        }
    }
}
