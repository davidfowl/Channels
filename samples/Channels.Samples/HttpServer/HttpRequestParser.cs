using System;
using Channels.Samples.Http;
using Channels.Text.Primitives;

namespace Channels.Samples
{
    public class HttpRequestParser
    {
        private ParsingState _state;

        public PreservedBuffer HttpVersion { get; set; }
        public PreservedBuffer Path { get; set; }
        public PreservedBuffer Method { get; set; }

        public RequestHeaderDictionary RequestHeaders = new RequestHeaderDictionary();

        public ParseResult ParseRequest(ref ReadableBuffer buffer)
        {
            if (_state == ParsingState.StartLine)
            {
                // Find \n
                ReadCursor delim;
                ReadableBuffer startLine;
                if (!buffer.TrySliceTo((byte)'\r', (byte)'\n', out startLine, out delim))
                {
                    return ParseResult.Incomplete;
                }

                // Move the buffer to the rest
                buffer = buffer.Slice(delim).Slice(2);

                ReadableBuffer method;
                if (!startLine.TrySliceTo((byte)' ', out method, out delim))
                {
                    return ParseResult.BadRequest;
                }

                Method = method.Preserve();

                // Skip ' '
                startLine = startLine.Slice(delim).Slice(1);

                ReadableBuffer path;
                if (!startLine.TrySliceTo((byte)' ', out path, out delim))
                {
                    return ParseResult.BadRequest;
                }

                Path = path.Preserve();

                // Skip ' '
                startLine = startLine.Slice(delim).Slice(1);

                var httpVersion = startLine;
                if (httpVersion.IsEmpty)
                {
                    return ParseResult.BadRequest;
                }

                HttpVersion = httpVersion.Preserve();

                _state = ParsingState.Headers;
            }

            // Parse headers
            // key: value\r\n

            while (!buffer.IsEmpty)
            {
                var headerName = default(ReadableBuffer);
                var headerValue = default(ReadableBuffer);

                // End of the header
                // \n
                ReadCursor delim;
                ReadableBuffer headerPair;
                if (!buffer.TrySliceTo((byte)'\r', (byte)'\n', out headerPair, out delim))
                {
                    return ParseResult.Incomplete;
                }

                buffer = buffer.Slice(delim).Slice(2);

                // End of headers
                if (headerPair.IsEmpty)
                {
                    return ParseResult.Complete;
                }

                // :
                if (!headerPair.TrySliceTo((byte)':', out headerName, out delim))
                {
                    return ParseResult.BadRequest;
                }

                headerName = headerName.TrimStart();
                headerPair = headerPair.Slice(delim).Slice(1);

                headerValue = headerPair.TrimStart();
                RequestHeaders.SetHeader(ref headerName, ref headerValue);
            }

            return ParseResult.Incomplete;
        }

        public void Reset()
        {
            _state = ParsingState.StartLine;

            Method.Dispose();
            Path.Dispose();
            HttpVersion.Dispose();

            RequestHeaders.Reset();
        }

        public enum ParseResult
        {
            Incomplete,
            Complete,
            BadRequest,
        }

        private enum ParsingState
        {
            StartLine,
            Headers
        }
    }
}
