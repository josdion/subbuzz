using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using subbuzz.Providers.Http;
using subbuzz.Providers.SubdlApi.Models;
using subbuzz.Providers.SubdlApi.Models.Responses;

namespace subbuzz.Providers.SubdlApi
{
    public static class SubDlApi
    {
        private const string BaseApiUrl = "https://api.subdl.com/api/v1";

        // header rate limits (5/1s & 240/1 min)
        private static int _hRemaining = -1;
        private static int _hReset = -1;
        // 40/10s limits
        private static DateTime _windowStart = DateTime.MinValue;
        private static int _requestCount;

        public static RequestHelper RequestHelperInstance { get; set; }

        public static async Task<ApiResponse<SearchResult>> SearchSubtitlesAsync(
            Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            var opts = new Dictionary<string, string> { { "api_key", apiKey } };
            foreach (var op in options)
            {
                opts.Add(op.Key.ToLowerInvariant(), op.Value.ToLowerInvariant());
            }

            var url = BuildQueryString("/subtitles", opts);
            HttpResponse response = await SendRequestAsync(url, HttpMethod.Get, null, null, 1, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<SearchResult>(response, $"url: {url}");
        }

        private static async Task<HttpResponse> SendRequestAsync(
            string endpoint,
            HttpMethod method,
            object body,
            Dictionary<string, string> headers,
            int attempt,
            CancellationToken cancellationToken
            )
        {
            if (headers == null)
                headers = new Dictionary<string, string>();


            if (_hRemaining == 0)
            {
                await Task.Delay(1000 * _hReset, cancellationToken).ConfigureAwait(false);
                _hRemaining = -1;
                _hReset = -1;
            }

            if (_requestCount == 40)
            {
                var diff = DateTime.UtcNow.Subtract(_windowStart).TotalSeconds;
                if (diff <= 10)
                {
                    await Task.Delay(1000 * (int)Math.Ceiling(10 - diff), cancellationToken).ConfigureAwait(false);
                    _hRemaining = -1;
                    _hReset = -1;
                }
            }

            if (DateTime.UtcNow.Subtract(_windowStart).TotalSeconds >= 10)
            {
                _windowStart = DateTime.UtcNow;
                _requestCount = 0;
            }

            var (response, responseHeaders, httpStatusCode) = await RequestHelperInstance.SendRequestAsync(BaseApiUrl + endpoint, method, body, headers, cancellationToken).ConfigureAwait(false);

            _requestCount++;

            if (responseHeaders.TryGetValue("x-ratelimit-remaining-second", out var value))
            {
                _ = int.TryParse(value, out _hRemaining);
            }

            if (responseHeaders.TryGetValue("ratelimit-reset", out value))
            {
                _ = int.TryParse(value, out _hReset);
            }

            if (httpStatusCode == (HttpStatusCode)429 /*HttpStatusCode.TooManyRequests*/ && attempt <= 4)
            {
                var time = _hReset == -1 ? 5 : _hReset;

                await Task.Delay(time * 1000, cancellationToken).ConfigureAwait(false);

                return await SendRequestAsync(endpoint, method, body, headers, attempt + 1, cancellationToken).ConfigureAwait(false);
            }

            if (httpStatusCode == HttpStatusCode.BadGateway && attempt <= 3)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);

                return await SendRequestAsync(endpoint, method, body, headers, attempt + 1, cancellationToken).ConfigureAwait(false);
            }

            if (!responseHeaders.TryGetValue("x-reason", out value))
            {
                value = string.Empty;
            }

            return new HttpResponse
            {
                Body = response,
                Code = httpStatusCode,
                Reason = value
            };
        }

        public static string BuildQueryString(string path, Dictionary<string, string> param)
        {
            if (param.Count == 0)
            {
                return path;
            }

            var url = new StringBuilder(path);
            url.Append('?');
            foreach (var op in param.OrderBy(x => x.Key))
            {
                url.Append(HttpUtility.UrlEncode(op.Key))
                    .Append('=')
                    .Append(HttpUtility.UrlEncode(op.Value))
                    .Append('&');
            }

            url.Length -= 1; // Remove last &
            return url.ToString();
        }
    }
}
