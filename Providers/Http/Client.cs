using SharpCompress.Common;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

#if EMBY
using SocketsHttpHandler = System.Net.Http.HttpClientHandler;
#endif

namespace subbuzz.Providers.Http
{
    public class Client
    {
        private int _maxRetries = 5;
        private int _maxRedirects = 3;
        private SocketsHttpHandler _handler;
        private HttpClient _httpClient;
        protected readonly Logger _logger;

        public Client(Logger logger)
        {
            _logger = logger;
            _handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                MaxAutomaticRedirections = _maxRedirects,
#if EMBY
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
#else
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
#endif
                UseCookies = true,
            };

            _httpClient = new HttpClient(_handler);
#if EMBY
            _httpClient.DefaultRequestHeaders.ConnectionClose = true;
#else
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            _httpClient.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");
#endif
        }

        public int Timeout
        {
            get => (int)_httpClient.Timeout.TotalSeconds;
            set => _httpClient.Timeout = TimeSpan.FromSeconds(value);
        }

        public int MaxRetries { get => _maxRetries; set => _maxRetries = value; }
        public int MaxRedirects { get => _maxRedirects; set => _maxRedirects = value; }

        public void AddDefaultRequestHeader(string name, string value) => _httpClient.DefaultRequestHeaders.Add(name, value);

        public async Task<Response> Send(Request req, CancellationToken cancellationToken)
        {
            int retry = 0;
            int redirect = 0;
            string redirectUrl = null;
            HttpMethod redirectMethod = null;

            while (true)
            {
                using var reqMsg = GetRequestMessage(req, redirectUrl, redirectMethod);
                _logger.LogDebug($"{reqMsg},\nDefault Headers:\n{{\n{_httpClient.DefaultRequestHeaders}}},\n{req}");

                using (HttpResponseMessage response = await _httpClient.SendAsync(reqMsg, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogDebug($"Response for {reqMsg.RequestUri}, {response}");
                    if (IsRedirect(response.StatusCode) && response.Headers.Location.AbsoluteUri.IsNotNullOrWhiteSpace() && redirect++ < _maxRedirects)
                    {
                        redirectUrl = response.Headers.Location.AbsoluteUri;
                        if (response.StatusCode == HttpStatusCode.RedirectMethod)
                            redirectMethod = HttpMethod.Get;

                        retry = 0;
                        continue;
                    }

                    if (IsRetriable(response.StatusCode) && retry++ < _maxRetries)
                    {
                        var waitTime = (response.StatusCode == (HttpStatusCode)429) ? 5000 : 1000;
                        await Task.Delay(retry * waitTime, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    return await GetRespInfo(response, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private HttpRequestMessage GetRequestMessage(Request req, string redirectUrl = null, HttpMethod redirectMethod = null)
        {
            var reqMsg = new HttpRequestMessage(redirectMethod ?? req.GetHttpMethod(), redirectUrl ?? req.Url);
            reqMsg.Headers.Host = reqMsg.RequestUri.Host;

            if (req.PostParams != null && req.PostParams.Count > 0)
                reqMsg.Content = new FormUrlEncodedContent(req.PostParams);

            reqMsg.Headers.Add("Referer", req.Referer);
            return reqMsg;
        }

        private async Task<Response> GetRespInfo(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var res = new Response
            {
                Info = new ResponseInfo
                {
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? "",
                    FileName = response.Content.Headers.ContentDisposition?.FileName ?? ""
                }
            };

            if (response.Content.Headers.ContentEncoding.Count > 0)
            {
                switch (response.Content.Headers.ContentEncoding.Last())
                {
                    case "gzip":
                        using (var gz = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress))
                        {
                            res.Content = new MemoryStream();
                            gz.CopyTo(res.Content);
                        }
                        break;
#if !EMBY
                    case "br":
                        using (var gz = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress))
                        {
                            res.Content = new MemoryStream();
                            gz.CopyTo(res.Content);
                        }
                        break;
#endif
                    case "deflate":
                        using (var gz = new DeflateStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress))
                        {
                            res.Content = new MemoryStream();
                            gz.CopyTo(res.Content);
                        }
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported HTTP Content Encoding: {response.Content.Headers.ContentEncoding.Last()}");
                }
            }
            else
            {
                res.Content = new MemoryStream();
                await response.Content.CopyToAsync(res.Content).ConfigureAwait(false);
            }

            res.Content.Seek(0, SeekOrigin.Begin);
            return res;
        }

        private bool IsRedirect(HttpStatusCode status)
        {
            return status == HttpStatusCode.MovedPermanently
                || status == HttpStatusCode.Redirect
                || status == HttpStatusCode.RedirectMethod
                || status == HttpStatusCode.RedirectKeepVerb
                || status == (HttpStatusCode)308 //PermanentRedirect
                ;
        }

        private bool IsRetriable(HttpStatusCode status)
        {
            return status == HttpStatusCode.ServiceUnavailable // for PodnapisiNet
                || status == HttpStatusCode.Conflict // for Subscene
                || status == HttpStatusCode.NotFound // for Subf2m
                || status == (HttpStatusCode)429 // TooManyRequests
                ;
        }
    }
}
