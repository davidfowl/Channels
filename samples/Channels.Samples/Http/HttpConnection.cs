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

        private static readonly byte[] _bytesTransferEncodingChunked = Encoding.ASCII.GetBytes("\r\nTransfer-Encoding: chunked");

        public HeaderDictionary RequestHeaders { get; } = new HeaderDictionary();
        public HeaderDictionary ResponseHeaders { get; } = new HeaderDictionary();

        public IReadableChannel Input { get; set; }

        public IWritableChannel Output { get; set; }

        private string HttpVersion { get; set; }

        // TODO: Check the http version
        internal bool KeepAlive => RequestHeaders.ContainsKey("Connection") && string.Equals(RequestHeaders["Connection"], "keep-alive");

        internal bool HasContentLength => ResponseHeaders.ContainsKey("Content-Length");
        internal bool HasTransferEncoding => ResponseHeaders.ContainsKey("Transfer-Encoding");

        internal IHttpApplication<TContext> Application;

        internal IWritableChannel Outgoing;

        private Exception ApplicationError;

        private bool AutoChunk;

        private ChannelStream InitialBody;

        internal async Task ProcessOutput(IReadableChannel incoming, IWritableChannel outgoing)
        {
            Outgoing = outgoing;

            while (true)
            {
                await incoming;

                var begin = incoming.BeginRead();

                if (begin.IsEnd && incoming.Completion.IsCompleted)
                {
                    break;
                }

                var buffer = outgoing.BeginWrite();

                WriteBeginResponseHeaders(ref buffer);

                try
                {
                    BufferSpan span;
                    while (begin.TryGetBuffer(out span))
                    {
                        if (AutoChunk)
                        {
                            ChunkWriter.WriteBeginChunkBytes(ref buffer, span.Length);
                            span.CopyTo(ref buffer);
                            ChunkWriter.WriteEndChunkBytes(ref buffer);
                        }
                        else
                        {
                            span.CopyTo(ref buffer);
                        }
                    }
                }
                finally
                {
                    incoming.EndRead(begin);
                }

                await outgoing.EndWriteAsync(buffer);
            }

            incoming.CompleteReading();
            outgoing.CompleteWriting();
        }

        private void WriteBeginResponseHeaders(ref WritableBuffer buffer)
        {
            if (HasStarted)
            {
                return;
            }

            HasStarted = true;

            ResponseHeaders["Server"] = "Channels HTTP Sample Server";
            ResponseHeaders["Date"] = DateTime.UtcNow.ToString("r");

            AutoChunk = !HasContentLength && !HasTransferEncoding && KeepAlive;

            if (AutoChunk)
            {
                ResponseHeaders["Transfer-Encoding"] = "chunked";
            }

            var httpVersion = Encoding.UTF8.GetBytes("HTTP/1.1 ");
            buffer.Write(httpVersion, 0, httpVersion.Length);
            var status = ReasonPhrases.ToStatusBytes(StatusCode);
            buffer.Write(status, 0, status.Length);

            foreach (var header in ResponseHeaders)
            {
                var headerRaw = "\r\n" + header.Key + ':' + header.Value;
                var headerBytes = Encoding.UTF8.GetBytes(headerRaw);
                buffer.Write(headerBytes, 0, headerBytes.Length);
            }

            var crlf = Encoding.UTF8.GetBytes("\r\n\r\n");
            buffer.Write(crlf, 0, crlf.Length);
        }

        private void WriteEndResponse(ref WritableBuffer buffer)
        {
            if (AutoChunk)
            {
                var chunkedEnding = Encoding.UTF8.GetBytes("0\r\n\r\n");
                buffer.Write(chunkedEnding, 0, chunkedEnding.Length);
            }
            else
            {
                var crlf = Encoding.UTF8.GetBytes("\r\n\r\n");
                buffer.Write(crlf, 0, crlf.Length);
            }
        }

        private async Task FinishResponse()
        {
            if (ApplicationError != null)
            {
                if (HasStarted)
                {
                    // Can't do anything
                    return;
                }

                StatusCode = 500;
            }

            var buffer = Outgoing.BeginWrite();

            WriteBeginResponseHeaders(ref buffer);

            WriteEndResponse(ref buffer);

            await Outgoing.EndWriteAsync(buffer);
        }

        private void Reset()
        {
            if (InitialBody == null)
            {
                InitialBody = new ChannelStream(Input, Output);
            }

            Body = InitialBody;
            RequestHeaders.Clear();
            ResponseHeaders.Clear();
            HasStarted = false;
            AutoChunk = false;
            StatusCode = 200;
        }

        internal async Task ProcessRequest()
        {
            Reset();

            while (true)
            {
                await Input;

                var begin = Input.BeginRead();
                var end = begin;

                bool needMoreData = true;

                if (begin.IsEnd && Input.Completion.IsCompleted)
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
                            continue;
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
                            continue;
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
                    Input.EndRead(end);
                }


                if (!needMoreData)
                {
                    var context = Application.CreateContext(this);

                    try
                    {
                        await Application.ProcessRequestAsync(context);
                    }
                    catch (Exception ex)
                    {
                        ApplicationError = ex;
                        Application.DisposeContext(context, ex);
                    }

                    break;
                }
            }

            await FinishResponse();
        }
    }
}
