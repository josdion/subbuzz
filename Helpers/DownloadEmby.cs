using MediaBrowser.Common.Net;
using MediaBrowser.Model.Net;
using subbuzz.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using ILogger = MediaBrowser.Model.Logging.ILogger;

namespace subbuzz.Helpers
{
    public partial class Download
    {
        private Random _rand = new Random();
        private readonly IHttpClient _httpClient;

        public Download(IHttpClient httpClient, ILogger logger, string provider)
        {
            _httpClient = httpClient;
            _logger = logger;
            _providerName = provider;
        }
        private Response GetRespInfo(HttpResponseInfo response)
        {
            string fileName = string.Empty;
            if (response.Headers.TryGetValue("Content-Disposition", out string contentDisposition))
            {
                var parts = contentDisposition.Split(';');
                if (parts.Length > 1 && parts[0].Trim().EqualsIgnoreCase("attachment"))
                {
                    foreach (var part in parts)
                    {
                        var names = part.Split('=');
                        if (names.Length > 1 && names[0].Trim().EqualsIgnoreCase("filename"))
                        {
                            fileName = names[1].Trim(' ', '"');
                        }
                    }
                }
            }

            var res = new Response
            {
                Content = new MemoryStream(),
                Info = new ResponseInfo
                {
                    ContentType = response.ContentType,
                    FileName = fileName
                }
            };

            // TODO: store to cache
            response.Content.CopyTo(res.Content);
            res.Content.Seek(0, SeekOrigin.Begin);
            return res;
        }

        private async Task<Response> Get(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken,
            int maxRetry = DefaultMaxRetry
            )
        {
            // TODO: check if cached

            var opts = new HttpRequestOptions
            {
                Url = link,
                UserAgent = UserAgent,
                Referer = referer,
                EnableKeepAlive = false,
                CancellationToken = cancellationToken,
                TimeoutMs = 30000, // in milliseconds
                DecompressionMethod = CompressionMethod.Gzip,
            };

            if (post_params != null && post_params.Count() > 0)
            {
                var postParamsEncode = new Dictionary<string, string>();
                var keys = new List<string>(post_params.Keys);
                foreach (string key in keys)
                {
                    if (key.IsNotNullOrWhiteSpace())
                        postParamsEncode[key] = HttpUtility.UrlEncode(post_params[key]);
                }
                opts.SetPostData(postParamsEncode);
            }

            int retry = 0;
            while (true)
            {
                try
                {
                    if (post_params != null && post_params.Count() > 0)
                    {
                        using (HttpResponseInfo response = await _httpClient.Post(opts).ConfigureAwait(false))
                        {
                            return GetRespInfo(response);
                        }
                    }
                    else
                    {
                        using (HttpResponseInfo response = await _httpClient.GetResponse(opts).ConfigureAwait(false))
                        {
                            return GetRespInfo(response);
                        }
                    }
                }
                catch (HttpException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.ServiceUnavailable // for PodnapisiNet
                        || ex.StatusCode == HttpStatusCode.Conflict // for Subscene
                        || ex.StatusCode == HttpStatusCode.NotFound // for Subf2m
                        || ex.StatusCode == (HttpStatusCode)429 // TooManyRequests
                        )
                    {
                        if (retry++ >= maxRetry) throw;
                        var waitTime = (ex.StatusCode == (HttpStatusCode)429) ? _rand.Next(4000, 5000) : _rand.Next(600, 1000);
                        await Task.Delay(retry * waitTime, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }
        }
    }
}
