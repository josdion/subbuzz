using MediaBrowser.Common.Extensions;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        private int _maxRedirects = 5;
        private readonly SocketsHttpHandler _handler;
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookies;
        protected readonly Logger _logger;

        public Client(Logger logger)
        {
            _logger = logger;
            _cookies = new CookieContainer();
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
                CookieContainer = _cookies,
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

        public void AddCookie(Cookie cookie) => _cookies.Add(cookie);

        public async Task<Response> SendFormAsync(FormRequest req, int? maxRetry, CancellationToken cancellationToken)
        {
            int retry = 0;
            int redirect = 0;

            Uri redirectUri = null;
            HttpMethod redirectMethod = null;

            while (true)
            {
                using var reqMsg = req.GetHttpRequestMessage(redirectUri?.AbsoluteUri, redirectMethod);
                //_logger.LogDebug($"{reqMsg},\nDefault Headers:\n{{\n{_httpClient.DefaultRequestHeaders}}}");
                using (HttpResponseMessage response = await _httpClient.SendAsync(reqMsg, cancellationToken).ConfigureAwait(false))
                {
                    //_logger.LogDebug($"Response for {reqMsg.RequestUri}, {response}");
                    if (IsRedirect(response.StatusCode) && response.Headers.Location.AbsoluteUri.IsNotNullOrWhiteSpace() && redirect++ < _maxRedirects)
                    {
                        redirectMethod = reqMsg.Method;
                        redirectUri = response.Headers.Location;

                        if (!redirectUri.IsAbsoluteUri)
                            redirectUri = new Uri(reqMsg.RequestUri, redirectUri);

                        if (response.StatusCode == HttpStatusCode.RedirectMethod)
                            redirectMethod = HttpMethod.Get;

                        _logger.LogInformation($"Redirect: {response.StatusCode} from: {reqMsg.RequestUri} to: {redirectUri}, method: {redirectMethod}");
                        continue;
                    }

                    if (IsRetriable(response.StatusCode) && retry++ < (maxRetry ?? _maxRetries))
                    {
                        var waitTime = (response.StatusCode == (HttpStatusCode)429) ? 5000 : 1000;
                        _logger.LogInformation($"Response code: {response.StatusCode}, Retry: {reqMsg.RequestUri} after {waitTime/1000} seconds");
                        await Task.Delay(retry * waitTime, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        throw new RateLimitExceededException($"TooManyRequests from: {reqMsg.RequestUri}");
                    }

                    response.EnsureSuccessStatusCode();
                    return await GetRespInfo(response, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        protected Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _httpClient.SendAsync(request, cancellationToken);

        private async Task<Response> GetRespInfo(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var res = new Response
            {
                Info = new ResponseInfo
                {
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? "",
                    FileName = response.Content.Headers.ContentDisposition?.FileName ?? "",
                }
            };

            if (response.Content.Headers.ContentEncoding.Count > 0)
            {
                var encoding = response.Content.Headers.ContentEncoding.Last();
                switch (encoding)
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

                response.Content.Headers.ContentEncoding.Remove(encoding);
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
