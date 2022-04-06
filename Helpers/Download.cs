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

namespace subbuzz.Helpers
{
    public partial class Download
    {
        private const string UrlSeparator = "*:*";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:98.0) Gecko/20100101 Firefox/98.0";

        public class ArchiveFileInfo : IDisposable
        {
            public string Name { get; set; }
            public string Ext { get; set; }
            public Stream Content { get; set; }
            public void Dispose() => Content?.Dispose();
        };

        public class ResponseInfo : IDisposable
        {
            public Stream Content { get; set; }
            public string ContentType { get; set; }
            public string FileName { get; set; }
            public void Dispose() => Content?.Dispose();
        };

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

            using (ResponseInfo resp = await Get(link, referer, post_params, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    using (IArchive arcreader = ArchiveFactory.Open(resp.Content))
                    {
                        foreach (IArchiveEntry entry in arcreader.Entries)
                        {
                            if (string.IsNullOrWhiteSpace(file) || file == entry.Key)
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
                }
                catch
                {
                    resp.Content.Seek(0, SeekOrigin.Begin);
                    string fileExt = resp.FileName.GetPathExtension();
                    Stream fileStream = SubtitleConvert.ToSupportedFormat(resp.Content, defaultEncoding, convertToUtf8, fps, ref fileExt);

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

        public async Task<List<(string fileName, string fileExt)>> GetArchiveFileNames(string link, string referer, CancellationToken cancellationToken)
        {
            var res = new List<(string fileName, string fileExt)>();
            var info = await GetArchiveFiles(link, referer, null, cancellationToken).ConfigureAwait(false);
            foreach (var entry in info) using (entry) res.Add((entry.Name, entry.Ext));
            return res;
        }

        public async Task<List<ArchiveFileInfo>> GetArchiveFiles(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken)
        {
            var res = new List<ArchiveFileInfo>();

            using (ResponseInfo resp = await Get(link, referer, post_params, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    using (IArchive arcreader = ArchiveFactory.Open(resp.Content))
                    {
                        foreach (IArchiveEntry entry in arcreader.Entries)
                        {
                            if (!entry.IsDirectory)
                            {
                                var info = new ArchiveFileInfo { Name = entry.Key, Ext = entry.Key.GetPathExtension().ToLower(), Content = new MemoryStream() };
                                Stream arcStream = entry.OpenEntryStream();
                                arcStream.CopyTo(info.Content);

                                res.Add(info);
                            }
                        }
                    }
                }
                catch
                {
                    var info = new ArchiveFileInfo { Name = resp.FileName, Ext = resp.FileName.GetPathExtension().ToLower(), Content = new MemoryStream() };
                    resp.Content.Seek(0, SeekOrigin.Begin);
                    resp.Content.CopyTo(info.Content);

                    res.Add(info);
                }
            }

            return res;
        }

        public async Task<Stream> GetStream(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken,
            int maxRetry = 5
            )
        {
            ResponseInfo resp = await Get(link, referer, post_params, cancellationToken, maxRetry);
            return resp.Content;
        }

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
