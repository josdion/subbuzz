using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace subbuzz.Helpers
{
    class Download
    {
        protected const string UrlSeparator = "*:*";
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0";

        public static string GetId(string link, string file, string lang, string fps)
        {
            return Utils.Base64UrlEncode(link + UrlSeparator + file + UrlSeparator + lang + UrlSeparator + fps);
        }

        public static async Task<SubtitleResponse> GetArchiveSubFile(
            IHttpClient httpClient, 
            string id, 
            string referer, 
            Encoding encoding)
        {
            string[] ids = Utils.Base64UrlDecode(id).Split(new[] { UrlSeparator }, StringSplitOptions.None);
            string link = ids[0];
            string file = ids[1];
            string lang = ids[2];

            bool convertToUtf8 = Plugin.Instance.Configuration.EncodeSubtitlesToUTF8;

            float fps = 25;
            try { fps = float.Parse(ids[3], CultureInfo.InvariantCulture); } catch { }

            Stream stream = await GetStream(httpClient, link, referer).ConfigureAwait(false);

            IArchive arcreader = ArchiveFactory.Open(stream);
            foreach (IArchiveEntry entry in arcreader.Entries)
            {
                if (file == entry.Key)
                {
                    Stream fileStream = entry.OpenEntryStream();

                    string fileExt = entry.Key.Split('.').LastOrDefault().ToLower();
                    if (fileExt != "srt" || (convertToUtf8 && encoding != Encoding.UTF8))
                    {
                        fileStream = SubtitleConvert.ToSrt(fileStream, encoding, convertToUtf8, fps);
                        fileExt = "srt";
                    }

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
                EnableKeepAlive = false,
#if EMBY
                TimeoutMs = 10000, //10 seconds timeout
#endif
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
