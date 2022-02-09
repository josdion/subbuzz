using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Net;
using SharpCompress.Archives;
using subbuzz.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

#if !EMBY
using System.IO.Compression;
using System.Net.Http;
#endif

namespace subbuzz.Helpers
{
    class ArchiveFileInfo
    {
        public string Name { get; set; }
        public string Ext { get; set; }
        public Stream Content { get; set; }
    };

    class Download
    {
        private const string UrlSeparator = "*:*";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0";

#if JELLYFIN
        private readonly HttpClient _httpClient;
        public Download(IHttpClientFactory http)
        {
            _httpClient = http.CreateClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
#else
        private readonly IHttpClient _httpClient;
        public Download(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }
#endif

        public static string GetId(string link, string file, string lang, string fps, Dictionary<string, string> post_params = null)
        {
            return Utils.Base64UrlEncode(
                link + UrlSeparator + // 0
                (String.IsNullOrEmpty(file) ? " " : file) + UrlSeparator + // 1
                lang + UrlSeparator + // 2
                SerializePostParams(post_params) + UrlSeparator + // 3
                (String.IsNullOrEmpty(fps) ? "25" : fps) // 4
            );
        }

        public async Task<SubtitleResponse> GetArchiveSubFile(
            string id,
            string referer,
            Encoding defaultEncoding,
            bool convertToUtf8,
            CancellationToken cancellationToken
            )
        {
            string[] ids = Utils.Base64UrlDecode(id).Split(new[] { UrlSeparator }, StringSplitOptions.None);
            string link = ids[0];
            string file = ids[1];
            string lang = ids[2];
            Dictionary<string, string> post_params = DeSerializePostParams(ids[3]);

            float fps = 25;
            try { fps = float.Parse(ids[4], CultureInfo.InvariantCulture); } catch { }

            using (Stream stream = await GetStream(link, referer, post_params, cancellationToken).ConfigureAwait(false))
            {
                IArchive arcreader = ArchiveFactory.Open(stream);
                foreach (IArchiveEntry entry in arcreader.Entries)
                {
                    if (String.IsNullOrWhiteSpace(file) || file == entry.Key)
                    {
                        Stream fileStream = entry.OpenEntryStream();

                        string fileExt = entry.Key.Split('.').LastOrDefault().ToLower();
                        fileStream = SubtitleConvert.ToSupportedFormat(fileStream, defaultEncoding, convertToUtf8, fps, ref fileExt);

                        return new SubtitleResponse
                        {
                            Language = lang,
                            Format = fileExt,
                            IsForced = false,
                            Stream = fileStream
                        };
                    }
                }
            }

            return new SubtitleResponse();
        }

        public async Task<List<string>> GetArchiveSubFileNames(string link, string referer, CancellationToken cancellationToken)
        {
            var res = new List<string>();

            using (Stream stream = await GetStream(link, referer, null, cancellationToken).ConfigureAwait(false))
            {
                IArchive arcreader = ArchiveFactory.Open(stream);
                foreach (IArchiveEntry entry in arcreader.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        res.Add(entry.Key);
                    }
                }
            }
            return res;
        }

        public async Task<List<ArchiveFileInfo>> GetArchiveSubFiles(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken)
        {
            var res = new List<ArchiveFileInfo>();

            using (Stream stream = await GetStream(link, referer, post_params, cancellationToken).ConfigureAwait(false))
            {
                IArchive arcreader = ArchiveFactory.Open(stream);
                foreach (IArchiveEntry entry in arcreader.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        Stream arcStream = entry.OpenEntryStream();
                        Stream memStream = new MemoryStream();
                        arcStream.CopyTo(memStream);

                        res.Add(new ArchiveFileInfo { Name = entry.Key, Ext = entry.Key.GetPathExtension(), Content = memStream });
                    }
                }
            }
            return res;
        }


#if JELLYFIN
        public async Task<Stream> GetStream(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken,
            int maxRetry = 5
            )
        {
            int retry = 0;
            HttpRequestMessage request;
            HttpResponseMessage response;

            while (true)
            {
                if (post_params != null && post_params.Count > 0)
                {
                    request = new HttpRequestMessage(HttpMethod.Post, link);
                    request.Content = new FormUrlEncodedContent(post_params);
                }
                else
                {
                    request = new HttpRequestMessage(HttpMethod.Get, link);
                }

                request.Headers.Add("Referer", referer);
                response = await _httpClient.SendAsync(request, cancellationToken);
                
                if (retry++ < maxRetry)
                {
                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        await Task.Delay(retry * 500, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                response.EnsureSuccessStatusCode();
                break;
            }

            Stream memStream = new MemoryStream();

            if (response.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                var gz = new GZipStream(response.Content.ReadAsStream(), CompressionMode.Decompress);
                gz.CopyTo(memStream);
            }
            else
            {
                await response.Content.CopyToAsync(memStream, cancellationToken);
            }

            // TODO: store to cache
            memStream.Seek(0, SeekOrigin.Begin);
            return memStream;
        }
#else
        public async Task<Stream> GetStream(
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
#if EMBY
                TimeoutMs = 30000, // in milliseconds
                DecompressionMethod = CompressionMethod.Gzip,
#else
                DecompressionMethod = CompressionMethods.Gzip,
#endif
            };

            HttpResponseInfo response;
            if (post_params != null && post_params.Count() > 0)
            {
#if EMBY
                var postParamsEncode = new Dictionary<string, string>();
                var keys = new List<string>(post_params.Keys);
                foreach (string key in keys)
                {
                    if (key.IsNotNullOrWhiteSpace())
                        postParamsEncode[key] = HttpUtility.UrlEncode(post_params[key]);
                }
                opts.SetPostData(postParamsEncode);
#else
                ByteArrayContent formUrlEncodedContent = new FormUrlEncodedContent(post_params);
                opts.RequestContent = await formUrlEncodedContent.ReadAsStringAsync();
                opts.RequestContentType = "application/x-www-form-urlencoded";
#endif

            }

            int retry = 0;
            while (true)
            {
                try
                {
                    if (post_params != null && post_params.Count() > 0)
                        response = await _httpClient.Post(opts).ConfigureAwait(false);
                    else
                        response = await _httpClient.GetResponse(opts).ConfigureAwait(false);

                    break;
                }
                catch (HttpException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        if (retry++ >= maxRetry) throw;
                        await Task.Delay(retry * 500, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }

#if !EMBY
            if (response.ContentHeaders.ContentEncoding.Contains("gzip"))
            {
                response.Content = new GZipStream(response.Content, CompressionMode.Decompress);
            }
#endif

            Stream memStream = new MemoryStream();
            response.Content.CopyTo(memStream);

            // TODO: store to cache
            memStream.Seek(0, SeekOrigin.Begin);
            return memStream;
        }
#endif

        private static string SerializePostParams(Dictionary<string, string> post_params)
        {
            string postData = " ";
            if (post_params != null && post_params.Count() > 0)
            {
                postData = string.Join(Environment.NewLine, post_params.Select(x => x.Key + UrlSeparator + x.Value).ToArray());
            }

            return Utils.Base64UrlEncode(postData);
        }

        private static Dictionary<string, string> DeSerializePostParams(string data)
        {
            string postData = Utils.Base64UrlDecode(data);
            if (postData.IsNotNullOrWhiteSpace())
            {
                var post_params = new Dictionary<string, string>();
                string[] items = postData.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                foreach (string item in items)
                {
                    string[] element = item.Split(new[] { UrlSeparator }, StringSplitOptions.None);
                    if (element.Count() == 2) post_params.Add(element[0], element[1]);
                }
                return post_params;
            }

            return null;
        }
    }
}
