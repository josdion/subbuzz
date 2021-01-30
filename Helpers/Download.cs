using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace subbuzz.Helpers
{
    class Download
    {
        protected const string _UrlSeparator = "*:*";
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0";

        public static string GetId(string link, string file, string lang)
        {
            return Utils.Base64UrlEncode(link + _UrlSeparator + file + _UrlSeparator + lang);
        }

        public static async Task<SubtitleResponse> GetArchiveSubFile(IHttpClient httpClient, string id, string referer)
        {
            string[] ids = Utils.Base64UrlDecode(id).Split(new[] { _UrlSeparator }, StringSplitOptions.None);
            string link = ids[0];
            string file = ids[1];
            string lang = ids[2];

            Stream stream = await GetStream(httpClient, link, referer).ConfigureAwait(false);

            IArchive arcreader = ArchiveFactory.Open(stream);
            foreach (IArchiveEntry entry in arcreader.Entries)
            {
                if (file == entry.Key)
                {
                    Stream fileStream = entry.OpenEntryStream();

                    string fileExt = entry.Key.Split('.').LastOrDefault().ToLower();
                    // TODO: if fileExt is sub, convert it to srt, as emby doesn't support sub files

                    return new SubtitleResponse
                    {
                        Language = lang,
                        Format = fileExt,
                        IsForced = false,
                        Stream = fileStream
                    };
                }
            }

            return new SubtitleResponse();
        }

        public static async Task<IEnumerable<string>> GetArchiveSubFileNames(IHttpClient httpClient, string link, string referer)
        {
            var res = new List<string>();

            using (Stream stream = await GetStream(httpClient, link, referer).ConfigureAwait(false))
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

        private static async Task<Stream> GetStream(IHttpClient httpClient, string link, string referer)
        {
            // TODO: check if cached

            var opts = new HttpRequestOptions
            {
                Url = link,
                UserAgent = UserAgent,
                Referer = referer,
                TimeoutMs = 10000, //10 seconds timeout
                EnableKeepAlive = false,
            };

            using (var response = await httpClient.Get(opts).ConfigureAwait(false))
            {
                Stream memStream = new MemoryStream();
                response.CopyTo(memStream);
                // TODO: store to cache
                memStream.Seek(0, SeekOrigin.Begin);
                return memStream;
            }
        }
    }
}
