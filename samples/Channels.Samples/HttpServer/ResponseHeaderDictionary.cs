using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Channels;
using Channels.Text.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.Extensions.Primitives;

namespace Channels.Samples.Http
{
    public class ResponseHeaderDictionary : IHeaderDictionary
    {
        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();
        private static readonly byte[] _serverHeaderBytes = Encoding.UTF8.GetBytes("\r\nServer: Channels");
        private static readonly byte[] _chunkedHeaderBytes = Encoding.UTF8.GetBytes("\r\nTransfer-Encoding: chunked");

        private static readonly byte[] _headersStartBytes = Encoding.UTF8.GetBytes("\r\n");
        private static readonly byte[] _headersSeperatorBytes = Encoding.UTF8.GetBytes(": ");
        private static readonly byte[] _headersEndBytes = Encoding.UTF8.GetBytes("\r\n\r\n");

        private readonly HeaderDictionary _headers = new HeaderDictionary();

        public StringValues this[string key]
        {
            get
            {
                return _headers[key];
            }

            set
            {
                _headers[key] = value;
            }
        }

        public int Count => _headers.Count;

        public bool IsReadOnly => false;

        public ICollection<string> Keys => _headers.Keys;

        public ICollection<StringValues> Values => _headers.Values;

        public void Add(KeyValuePair<string, StringValues> item) => _headers.Add(item);

        public void Add(string key, StringValues value) => _headers.Add(key, value);

        public void Clear()
        {
            _headers.Clear();
        }

        public bool Contains(KeyValuePair<string, StringValues> item)
        {
            return _headers.Contains(item);
        }

        public bool ContainsKey(string key)
        {
            return _headers.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            _headers.CopyTo(array, arrayIndex);
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            return _headers.GetEnumerator();
        }

        public bool Remove(KeyValuePair<string, StringValues> item)
        {
            return _headers.Remove(item);
        }

        public bool Remove(string key)
        {
            return _headers.Remove(key);
        }

        public bool TryGetValue(string key, out StringValues value)
        {
            return _headers.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public unsafe void CopyTo(bool chunk, ref WritableBuffer buffer)
        {
            foreach (var header in _headers)
            {
                buffer.Write(new Span<byte>(_headersStartBytes, 0, _headersStartBytes.Length));
                WritableBufferExtensions.WriteAsciiString(ref buffer, header.Key);
                buffer.Write(new Span<byte>(_headersSeperatorBytes, 0, _headersSeperatorBytes.Length));
                WritableBufferExtensions.WriteAsciiString(ref buffer, header.Value);
            }

            if (chunk)
            {
                buffer.Write(new Span<byte>(_chunkedHeaderBytes, 0, _chunkedHeaderBytes.Length));
            }

            buffer.Write(new Span<byte>(_serverHeaderBytes, 0, _serverHeaderBytes.Length));
            var date = _dateHeaderValueManager.GetDateHeaderValues().Bytes;
            buffer.Write(new Span<byte>(date, 0, date.Length));

            buffer.Write(new Span<byte>(_headersEndBytes, 0, _headersEndBytes.Length));
        }

        public void Reset() => _headers.Clear();
    }
}
