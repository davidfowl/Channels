using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Channels.Samples.Http
{
    public class RequestHeaderDictionary : IHeaderDictionary
    {
        private Dictionary<string, ReadableBuffer> _headerSlices = new Dictionary<string, ReadableBuffer>();
        private HeaderDictionary _headers = new HeaderDictionary();

        public StringValues this[string key]
        {
            get
            {
                StringValues values;
                TryGetValue(key, out values);
                return values;
            }

            set
            {
                SetHeader(key, value);
            }
        }

        public int Count => _headerSlices.Count;

        public bool IsReadOnly => false;

        public ICollection<string> Keys => _headerSlices.Keys;

        public ICollection<StringValues> Values => _headerSlices.Values.Select(v => new StringValues(v.GetUtf8String())).ToList();

        public void SetHeader(ref ReadableBuffer key, ref ReadableBuffer value)
        {
            _headerSlices[key.GetAsciiString()] = value.Clone();
        }

        private void SetHeader(string key, StringValues value)
        {
            _headers[key] = value;
        }

        public void Add(KeyValuePair<string, StringValues> item)
        {
            SetHeader(item.Key, item.Value);
        }

        public void Add(string key, StringValues value)
        {
            SetHeader(key, value);
        }

        public void Clear()
        {
            _headers.Clear();
        }

        public bool Contains(KeyValuePair<string, StringValues> item)
        {
            return false;
        }

        public bool ContainsKey(string key)
        {
            return _headers.ContainsKey(key) || _headerSlices.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public void Reset()
        {
            foreach (var pair in _headerSlices)
            {
                pair.Value.Dispose();
            }

            _headerSlices.Clear();
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            foreach (var pair in _headerSlices)
            {
                if (!_headers.ContainsKey(pair.Key))
                {
                    _headers[pair.Key] = pair.Value.GetAsciiString();
                }
            }

            return _headers.GetEnumerator();
        }

        public bool Remove(KeyValuePair<string, StringValues> item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(string key)
        {
            return _headers.Remove(key);
        }

        public bool TryGetValue(string key, out StringValues value)
        {
            if (_headers.TryGetValue(key, out value))
            {
                return true;
            }

            ReadableBuffer buffer;
            if (_headerSlices.TryGetValue(key, out buffer))
            {
                value = buffer.GetAsciiString();
                _headers[key] = value;
                return true;
            }

            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
