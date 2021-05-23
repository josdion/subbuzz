using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives;
using subbuzz.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        public string Ext { get; set;  }
        public Stream Content { get; set; }
    };

    class Download
    {
        private const string UrlSeparator = "*:*";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0";

#if JELLYFIN_10_7
        private readonly HttpClient _httpClient;
        public Download(IHttpClientFactory http)
        {
            _httpClient = http.CreateClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.Add("Pragma","no-cache");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding","gzip");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
#else
        private readonly IHttpClient _httpClient;
        public Download(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }
#endif

        public static string GetId(string link, string file, string lang, string fps)
        {
            return Utils.Base64UrlEncode(
                link + UrlSeparator + 
                (String.IsNullOrEmpty(file) ? " " : file) + UrlSeparator + 
                lang + UrlSeparator +
                (String.IsNullOrEmpty(fps) ? "25" : fps)
            );
        }

        public async Task<SubtitleResponse> GetArchiveSubFile(
            string id,
            string referer,
            Encoding encoding,
            CancellationToken cancellationToken
            )
        {
            string[] ids = Utils.Base64UrlDecode(id).Split(new[] { UrlSeparator }, StringSplitOptions.None);
            string link = ids[0];
            string file = ids[1];
            string lang = ids[2];

            bool convertToUtf8 = Plugin.Instance.Configuration.EncodeSubtitlesToUTF8;

            float fps = 25;
            try { fps = float.Parse(ids[3], CultureInfo.InvariantCulture); } catch { }

            using (Stream stream = await GetStream(link, referer, null, cancellationToken).ConfigureAwait(false))
            {
                IArchive arcreader = ArchiveFactory.Open(stream);
                foreach (IArchiveEntry entry in arcreader.Entries)
                {
                    if (String.IsNullOrWhiteSpace(file) || file == entry.Key)
                    {
                        Stream fileStream = entry.OpenEntryStream();

                        string fileExt = entry.Key.Split('.').LastOrDefault().ToLower();
                        fileStream = SubtitleConvert.ToSupportedFormat(fileStream, encoding, convertToUtf8, fps, ref fileExt);

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

        public async Task<IEnumerable<string>> GetArchiveSubFileNames(string link, string referer, CancellationToken cancellationToken)
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

        public async Task<List<ArchiveFileInfo>> GetArchiveSubFiles(string link, string referer, CancellationToken cancellationToken)
        {
            var res = new List<ArchiveFileInfo>();

            using (Stream stream = await GetStream(link, referer, null, cancellationToken).ConfigureAwait(false))
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


#if JELLYFIN_10_7
        public async Task<Stream> GetStream(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken
            )
        {
            HttpRequestMessage request;
            HttpResponseMessage response;

            if (post_params != null)
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
            CancellationToken cancellationToken
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
            if (post_params != null)
            {
#if EMBY
                var keys = new List<string>(post_params.Keys);
                foreach (string key in keys) post_params[key] = HttpUtility.UrlEncode(post_params[key]);
                opts.SetPostData(post_params);
#else
                ByteArrayContent formUrlEncodedContent = new FormUrlEncodedContent(post_params);
                opts.RequestContent = await formUrlEncodedContent.ReadAsStringAsync();
                opts.RequestContentType = "application/x-www-form-urlencoded";
#endif
                response = await _httpClient.Post(opts).ConfigureAwait(false);
            }
            else response = await _httpClient.GetResponse(opts).ConfigureAwait(false);

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
    }
}
