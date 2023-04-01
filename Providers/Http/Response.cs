using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace subbuzz.Providers.Http
{
    public class ResponseInfo
    {
        public string ContentType { get; set; }
        public string FileName { get; set; }
    };

    public class Response : IDisposable
    {
        public bool Cached = false;
        public Stream Content { get; set; }
        public ResponseInfo Info { get; set; }
        public Dictionary<string, string> Cookies { get; set; }
        public void Dispose() => Content?.Dispose();
    };
}
