using MediaBrowser.Common.Net;
using MediaBrowser.Model.Net;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers.OpenSubtitlesAPI
{
    public class RequestHelper
    {
        private const string _userAgent = "subbuzz";
        private readonly string _version;

#if JELLYFIN_10_7
        private readonly IHttpClientFactory _clientFactory;
        public RequestHelper(IHttpClientFactory factory, string version)
        {
            _clientFactory = factory;
            _version = version;
        }
#else
        private readonly IHttpClient _httpClient;
        public RequestHelper(IHttpClient httpClient, string version)
        {
            _httpClient = httpClient;
            _version = version;
        }
#endif

        public static RequestHelper? Instance { get; set; }

        public static string ComputeHash(Stream stream)
        {
            var hash = ComputeMovieHash(stream);
            return Utils.ByteArrayToString(hash);
        }

        private static byte[] ComputeMovieHash(Stream input)
        {
            using (input)
            {
                long streamSize = input.Length, lHash = streamSize;
                int size = sizeof(long), count = 65536 / size;
                var buffer = new byte[size];

                for (int i = 0; i < count && input.Read(buffer, 0, size) > 0; i++)
                {
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                input.Position = Math.Max(0, streamSize - 65536);

                for (int i = 0; i < count && input.Read(buffer, 0, size) > 0; i++)
                {
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                var result = BitConverter.GetBytes(lHash);
                Array.Reverse(result);

                return result;
            }
        }

#if JELLYFIN_10_7

        internal async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(
            string url, 
            HttpMethod method, 
            object? body, 
            Dictionary<string, string> headers, 
            CancellationToken cancellationToken
            )
        {
            var client = _clientFactory.CreateClient("Default");
            client.Timeout = TimeSpan.FromSeconds(30);

            HttpContent? content = null;
            if (method != HttpMethod.Get && body != null)
            {
                content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            using var request = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content,
                Headers =
                {
                    UserAgent = { new ProductInfoHeaderValue(_userAgent, _version) },
                    Accept = { new MediaTypeWithQualityHeaderValue("*/*") }
                }
            };

            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, "authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
                }
                else
                {
                    request.Headers.Add(key, value);
                }
            }

            var result = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var resHeaders = result.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value.First());
            var resBody = await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return (resBody, resHeaders, result.StatusCode);
        }
#else

        internal async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(
            string url,
            HttpMethod method,
            object? body,
            Dictionary<string, string> headers,
            CancellationToken cancellationToken
            )
        {
            var request = new HttpRequestOptions
            {
                Url = url,
                UserAgent = _userAgent,
                AcceptHeader = new MediaTypeWithQualityHeaderValue("*/*").ToString(),
                TimeoutMs = 30000, // in milliseconds
                CancellationToken = cancellationToken,
            };

            if (method != HttpMethod.Get && body != null)
            {
                request.RequestHttpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            foreach (var (key, value) in headers)
            {
                if (string.Equals(key, "authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.RequestHeaders.Add("authorization", new AuthenticationHeaderValue("Bearer", value).ToString());
                }
                else
                {
                    request.RequestHeaders.Add(key, value);
                }
            }

            try
            {
                var result = await _httpClient.SendAsync(request, method.ToString()).ConfigureAwait(false);
                StreamReader reader = new StreamReader(result.Content);
                var resBody = await reader.ReadToEndAsync().ConfigureAwait(false);

                return (resBody, result.Headers, result.StatusCode);
            }
            catch (HttpException e)
            {
                return (e.ToString(), null, e.StatusCode ?? HttpStatusCode.Forbidden);
            }
        }
#endif
    }
}
