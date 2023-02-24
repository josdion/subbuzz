using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Helpers
{
    public partial class Download
    {
        private Random _rand = new Random();
        private readonly HttpClient _httpClient;
        public Download(IHttpClientFactory http)
        {
            _httpClient = http.CreateClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.7,bg;q=0.3");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private async Task<ResponseInfo> GetRespInfo(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var res = new ResponseInfo
            {
                Content = new MemoryStream(),
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "",
                FileName = response.Content.Headers.ContentDisposition?.FileName ?? ""
            };

            if (response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                using (var gz = new GZipStream(response.Content.ReadAsStream(cancellationToken), CompressionMode.Decompress))
                {
                    gz.CopyTo(res.Content);
                }
            }
            else
            if (response.Content.Headers.ContentEncoding.Contains("br"))
            {
                using (var gz = new BrotliStream(response.Content.ReadAsStream(cancellationToken), CompressionMode.Decompress))
                {
                    gz.CopyTo(res.Content);
                }
            }
            else
            if (response.Content.Headers.ContentEncoding.Contains("deflate"))
            {
                using (var gz = new DeflateStream(response.Content.ReadAsStream(cancellationToken), CompressionMode.Decompress))
                {
                    gz.CopyTo(res.Content);
                }
            }
            else
            {
                await response.Content.CopyToAsync(res.Content, cancellationToken).ConfigureAwait(false);
            }

            // TODO: store to cache
            res.Content.Seek(0, SeekOrigin.Begin);
            return res;
        }

        private async Task<ResponseInfo> Get(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken,
            int maxRetry = 5
            )
        {
            int retry = 0;

            while (true)
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, link);
                request.Headers.Host = request.RequestUri.Host;
                request.Headers.Connection.ParseAdd("keep-alive");

                if (post_params != null && post_params.Count > 0)
                {
                    request.Method = HttpMethod.Post;
                    request.Content = new FormUrlEncodedContent(post_params);
                }

                request.Headers.Add("Referer", referer);
                using (HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken))
                {
                    if (retry++ < maxRetry)
                    {
                        if (response.StatusCode == HttpStatusCode.ServiceUnavailable // for PodnapisiNet
                            || response.StatusCode == HttpStatusCode.Conflict // for Subscene
                            || response.StatusCode == HttpStatusCode.NotFound // for Subf2m
                            || response.StatusCode == (HttpStatusCode)429 // TooManyRequests
                            )
                        {
                            var waitTime = (response.StatusCode == (HttpStatusCode)429) ? _rand.Next(4000, 5000) : _rand.Next(600, 1000);
                            await Task.Delay(retry * waitTime, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.Moved || response.StatusCode == HttpStatusCode.Found)
                        {
                            return await Get(response.Headers.Location.AbsoluteUri, referer, post_params, cancellationToken, maxRetry - retry);
                        }
                    }

                    response.EnsureSuccessStatusCode();
                    return await GetRespInfo(response, cancellationToken).ConfigureAwait(false);
                }
            }
        }

    }
}
