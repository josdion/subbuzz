using MediaBrowser.Common.Net;
using MediaBrowser.Model.Net;
using subbuzz.Extensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace subbuzz.Helpers
{
    public partial class Download
    {
        private readonly IHttpClient _httpClient;
        public Download(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        private ResponseInfo GetRespInfo(HttpResponseInfo response)
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
                            fileName = names[1].Trim();
                        }
                    }
                }
            }

            var res = new ResponseInfo
            {
                Content = new MemoryStream(),
                ContentType = response.ContentType,
                FileName = fileName
            };

            // TODO: store to cache
            response.Content.CopyTo(res.Content);
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
                    if (ex.StatusCode == HttpStatusCode.ServiceUnavailable || // for PodnapisiNet
                        ex.StatusCode == HttpStatusCode.Conflict) // for Subscene
                    {
                        if (retry++ >= maxRetry) throw;
                        await Task.Delay(retry * 500, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }
        }
    }
}
