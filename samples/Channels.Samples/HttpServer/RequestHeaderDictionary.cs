using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Channels.Text.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Channels.Samples.Http
{
    public class RequestHeaderDictionary : IHeaderDictionary
    {
        private Dictionary<string, HeaderValue> _headers = new Dictionary<string, HeaderValue>();

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

        public int Count => _headers.Count;

        public bool IsReadOnly => false;

        public ICollection<string> Keys => _headers.Keys;

        public ICollection<StringValues> Values => _headers.Values.Select(v => v.GetValue()).ToList();

        public void SetHeader(ref ReadableBuffer key, ref ReadableBuffer value)
        {
            _headers[key.GetAsciiString()] = new HeaderValue
            {
                Raw = value.Preserve()
            };
        }

        private void SetHeader(string key, StringValues value)
        {
            _headers[key] = new HeaderValue
            {
                Value = value
            };
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
            return _headers.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        public void Reset()
        {
            foreach (var pair in _headers)
            {
                pair.Value.Raw?.Dispose();
            }

            _headers.Clear();
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            return _headers.Select(h => new KeyValuePair<string, StringValues>(h.Key, h.Value.GetValue())).GetEnumerator();
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
            HeaderValue headerValue;
            if (_headers.TryGetValue(key, out headerValue))
            {
                value = headerValue.GetValue();
                return true;
            }

            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private struct HeaderValue
        {
            public ReadableBuffer? Raw;
            public StringValues? Value;

            public StringValues GetValue()
            {
                if (!Value.HasValue)
                {
                    if (!Raw.HasValue)
                    {
                        return StringValues.Empty;
                    }

                    Value = Raw.Value.GetAsciiString();
                }

                return Value.Value;
            }
        }
    }
}
