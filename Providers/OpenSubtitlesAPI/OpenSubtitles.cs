using subbuzz.Providers.OpenSubtitlesAPI.Models;
using subbuzz.Providers.OpenSubtitlesAPI.Models.Responses;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers.OpenSubtitlesAPI
{
    public static class OpenSubtitles
    {
        private const string BaseApiUrl = "https://api.opensubtitles.com/api/v1";

        // header rate limits (5/1s & 240/1 min)
        private static int _hRemaining = -1;
        private static int _hReset = -1;
        // 40/10s limits
        private static DateTime _windowStart = DateTime.MinValue;
        private static int _requestCount;

        public static async Task<ApiResponse<LoginInfo>> LogInAsync(string username, string password, string apiKey, CancellationToken cancellationToken)
        {
            var body = new { username, password };
            var response = await SendRequestAsync("/login", HttpMethod.Post, body, null, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<LoginInfo>(response);
        }

        public static async Task<bool> LogOutAsync(LoginInfo user, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(user.Token))
            {
                throw new ArgumentNullException(nameof(user.Token), "Token is null or empty");
            }

            var headers = new Dictionary<string, string> { { "Authorization", user.Token } };

            var response = await SendRequestAsync("/logout", HttpMethod.Delete, null, headers, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<object>(response).Ok;
        }

        public static async Task<ApiResponse<UserInfoData>> GetUserInfo(LoginInfo user, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(user.Token))
            {
                throw new ArgumentNullException(nameof(user.Token), "Token is null or empty");
            }

            var headers = new Dictionary<string, string> { { "Authorization", user.Token } };

            var response = await SendRequestAsync("/infos/user", HttpMethod.Get, null, headers, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<UserInfoData>(response);
        }

        public static async Task<ApiResponse<DownloadInfo>> GetSubtitleLinkAsync(int file, string token, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentNullException(nameof(token), "Token is null or empty");
            }

            var headers = new Dictionary<string, string> { { "Authorization", token } };

            var body = new { file_id = file };
            var response = await SendRequestAsync("/download", HttpMethod.Post, body, headers, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<DownloadInfo>(response, $"file id: {file}");
        }

        public static async Task<ApiResponse<Stream>> DownloadSubtitleAsync(string url, CancellationToken cancellationToken)
        {
            //var response = await SendRequestAsync(url, HttpMethod.Get, null, null, null, cancellationToken).ConfigureAwait(false);
            var (stream, _, httpStatusCode) = await RequestHelper.Instance!.SendRequestAsyncStream(url, HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<Stream>(stream, new HttpResponse { Code = httpStatusCode });
        }

        public static async Task<ApiResponse<IReadOnlyList<ResponseData>>> SearchSubtitlesAsync(Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            var opts = System.Web.HttpUtility.ParseQueryString(string.Empty);

            foreach (var (key, value) in options.OrderBy(x => x.Key))
            {
                opts.Add(key, value);
            }

            var max = -1;
            var current = 1;

            List<ResponseData> final = new List<ResponseData>();
            ApiResponse<SearchResult> last;
            HttpResponse response;

            do
            {
                opts.Set("page", current.ToString(CultureInfo.InvariantCulture));

                response = await SendRequestAsync($"/subtitles?{opts}", HttpMethod.Get, null, null, apiKey, cancellationToken).ConfigureAwait(false);

                last = new ApiResponse<SearchResult>(response, $"options: {options}", $"page: {current}");

                if (!last.Ok || last.Data == null)
                {
                    break;
                }

                if (last.Data.TotalPages == 0)
                {
                    break;
                }

                if (max == -1)
                {
                    max = last.Data.TotalPages;
                }

                current = last.Data.Page + 1;

                final.AddRange(last.Data.Data);
            }
            while (current <= max && last.Data.Data.Count == 100);

            return new ApiResponse<IReadOnlyList<ResponseData>>(final, response);
        }

        private static async Task<HttpResponse> SendRequestAsync(
            string endpoint,
            HttpMethod method,
            object? body,
            Dictionary<string, string>? headers,
            string? apiKey,
            CancellationToken cancellationToken
            )
        {
            var url = endpoint.StartsWith('/') ? BaseApiUrl + endpoint : endpoint;
            var isFullApiUrl = url.StartsWith(BaseApiUrl, StringComparison.OrdinalIgnoreCase);

            headers ??= new Dictionary<string, string>();

            if (isFullApiUrl)
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new ArgumentException("Provided API key is blank", nameof(apiKey));
                }

                if (!headers.ContainsKey("Api-Key"))
                {
                    headers.Add("Api-Key", apiKey);
                }

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
            }

            var (response, responseHeaders, httpStatusCode) = await RequestHelper.Instance!.SendRequestAsync(url, method, body, headers, cancellationToken).ConfigureAwait(false);

            if (!isFullApiUrl || responseHeaders == null)
            {
                return new HttpResponse
                {
                    Body = response,
                    Code = httpStatusCode
                };
            }

            _requestCount++;

            if (responseHeaders.TryGetValue("x-ratelimit-remaining-second", out var value))
            {
                _ = int.TryParse(value, out _hRemaining);
            }

            if (responseHeaders.TryGetValue("ratelimit-reset", out value))
            {
                _ = int.TryParse(value, out _hReset);
            }

            if (httpStatusCode != HttpStatusCode.TooManyRequests)
            {
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

            var time = _hReset == -1 ? 5 : _hReset;

            await Task.Delay(time * 1000, cancellationToken).ConfigureAwait(false);

            return await SendRequestAsync(endpoint, method, body, headers, apiKey, cancellationToken).ConfigureAwait(false);
        }

    }
}
