using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers.Http
{
    public class RequestHelper : Http.Client
    {
        private const string _userAgent = "subbuzz";

        public RequestHelper(Logger logger, string version) : base(logger) 
        {
            AddDefaultRequestHeader("User-Agent", $"{_userAgent}/{version}");
            AddDefaultRequestHeader("Accept", "*/*");
        }


        internal async Task<(Stream, Dictionary<string, string>, HttpStatusCode)> SendRequestAsyncStream(
            string url,
            HttpMethod method,
            object body,
            Dictionary<string, string> headers,
            CancellationToken cancellationToken
            )
        {
            HttpContent content = null;
            if (method != HttpMethod.Get && body != null)
            {
                content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var request = new HttpRequestMessage
            {
                Method = method,
                RequestUri = new Uri(url),
                Content = content,
            };

            headers ??= new Dictionary<string, string>();
            foreach (var hdr in headers)
            {
                if (string.Equals(hdr.Key, "authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hdr.Value);
                }
                else
                {
                    request.Headers.Add(hdr.Key, hdr.Value);
                }
            }

            var result = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var resHeaders = result.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value.First());

            return (await result.Content.ReadAsStreamAsync().ConfigureAwait(false), resHeaders, result.StatusCode);
        }

        internal async Task<(string, Dictionary<string, string>, HttpStatusCode)> SendRequestAsync(
            string url,
            HttpMethod method,
            object body,
            Dictionary<string, string> headers,
            CancellationToken cancellationToken
            )
        {
            var (resStram, resHeaders, resCode) = await SendRequestAsyncStream(url, method, body, headers, cancellationToken);

            StreamReader reader = new StreamReader(resStram);
            var resBody = await reader.ReadToEndAsync().ConfigureAwait(false);

            return (resBody, resHeaders, resCode);
        }

    }
}
