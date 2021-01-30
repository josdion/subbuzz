using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Subtitles;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace subbuzz.Helpers
{
    class Download
    {
        public const string UrlSeparator = "*:*";
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0";

        public static async Task<SubtitleResponse> ArchiveSubFile(IHttpClient httpClient, string urlid, string referer)
        {
            string[] ids = Utils.Base64UrlDecode(urlid).Split(new[] { UrlSeparator }, StringSplitOptions.None);
            string link = ids[0];
            string file = ids[1];
            string lang = ids[2];

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
                var arcreader = ReaderFactory.Open(response);
                while (arcreader.MoveToNextEntry())
                {
                    if (file == arcreader.Entry.Key)
                    {
                        byte[] buf = new byte[arcreader.Entry.Size];
                        arcreader.OpenEntryStream().Read(buf, 0, buf.Length);

                        string fileExt = arcreader.Entry.Key.Split('.').LastOrDefault().ToLower();
                        // TODO: if fileExt is sub, convert it to srt, as emby doesn't support sub files

                        return new SubtitleResponse
                        {
                            Language = lang,
                            Format = fileExt,
                            IsForced = false,
                            Stream = new MemoryStream(buf)
                        };
                    }
                }
            }

            return new SubtitleResponse();
        }

        public static async Task<IEnumerable<string>> ArchiveSubFileNames(IHttpClient httpClient, string link, string referer)
        {
            var res = new List<string>();

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
                var arcreader = ReaderFactory.Open(response);
                while (arcreader.MoveToNextEntry())
                {
                    if (!arcreader.Entry.IsDirectory)
                    {
                        res.Add(arcreader.Entry.Key);
                    }
                }
            }

            return res;
        }

    }
}
