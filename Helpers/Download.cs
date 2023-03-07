using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives;
using SharpCompress.Common;
using subbuzz.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Helpers
{
    public partial class Download
    {
        private const int DefaultMaxRetry = 5;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0";

        private FileCache _cache = null;

        public class Link
        {
            public string Url { get; set; } 
            public Dictionary<string, string> PostParams { get; set; }
            public string CacheKey { get; set; }
            public string CacheRegion { get; set; }
        }

        public class LinkSub : Link 
        {
            public string File { get; set; }
            public string Lang { get; set; }
            public string Fps { get; set; }

            public string GetId()
            {
                byte[] encbuff = JsonSerializer.SerializeToUtf8Bytes(this, this.GetType());
                return Convert.ToBase64String(encbuff).Replace("=", ",").Replace("+", "-").Replace("/", "_");
            }

            public static LinkSub FromId(string id)
            {
                if (id.IsNotNullOrWhiteSpace())
                {
                    byte[] decbuff = Convert.FromBase64String(id.Replace(",", "=").Replace("-", "+").Replace("_", "/"));
                    return JsonSerializer.Deserialize<LinkSub>(decbuff);
                }

                return default;
            }

            public float GetFps()
            {
                try 
                { 
                    return float.Parse(Fps, CultureInfo.InvariantCulture); 
                } 
                catch 
                {
                    return 25; 
                }
            }
        }

        public class ArchiveFileInfo : IDisposable
        {
            public string Name { get; set; }
            public string Ext { get; set; }
            public Stream Content { get; set; }
            public void Dispose() => Content?.Dispose();
        };

        public class ArchiveFileInfoList : List<ArchiveFileInfo>, IDisposable
        {
            public void Dispose()
            {
                foreach (var item in this)
                {
                    item.Dispose();
                }
            }
        }

        public class ResponseInfo
        {
            public string ContentType { get; set; }
            public string FileName { get; set; }
        };

        public class Response : IDisposable
        {
            public bool Cached = false;
            public Stream Content { get; set; }
            public ResponseInfo Info { get; set; }
            public void Dispose() => Content?.Dispose();
        };

        public static string GetId(string link, string file, string lang, string fps, Dictionary<string, string> post_params = null)
        {
            LinkSub sub = new LinkSub
            {
                Url = link,
                PostParams = post_params,
                CacheKey = link,
                CacheRegion = "sub",
                File = file,
                Lang = lang,
                Fps = fps,
            };
            return sub.GetId();
        }

        public async Task<SubtitleResponse> GetArchiveSubFile(
            string id,
            string referer,
            Encoding defaultEncoding,
            SubPostProcessingCfg postProcessing,
            CancellationToken cancellationToken
            )
        {
            LinkSub link = LinkSub.FromId(id);
            using (ArchiveFileInfoList files = await GetArchiveFiles(link, referer, cancellationToken))
            {
                foreach (ArchiveFileInfo file in files)
                {
                    if (string.IsNullOrWhiteSpace(link.File) || link.File == file.Name)
                    {
                        string fileExt = file.Ext;
                        Stream fileStream = SubtitleConvert.ToSupportedFormat(file.Content, defaultEncoding, link.GetFps(), ref fileExt, postProcessing);

                        return new SubtitleResponse
                        {
                            Language = link.Lang,
                            Format = fileExt,
                            IsForced = false,
                            Stream = fileStream
                        };
                    }
                }
            }

            return new SubtitleResponse();
        }

        public async Task<List<(string fileName, string fileExt)>> GetArchiveFileNames(string link, string referer, CancellationToken cancellationToken)
        {
            var res = new List<(string fileName, string fileExt)>();
            using (var info = await GetArchiveFiles(link, referer, null, cancellationToken).ConfigureAwait(false))
            {
                foreach (var entry in info) using (entry) res.Add((entry.Name, entry.Ext));
            }
            return res;
        }

        public async Task<ArchiveFileInfoList> GetArchiveFiles(
            string url,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken)
        {
            Link link = new Link 
            { 
                Url = url, 
                PostParams = post_params, 
                CacheKey = url, 
                CacheRegion = "sub", 
            };

            return await GetArchiveFiles(link, referer, cancellationToken);
        }

        public async Task<ArchiveFileInfoList> GetArchiveFiles(Link link, string referer, CancellationToken cancellationToken)
        {
            var res = new ArchiveFileInfoList();

            using (Response resp = await GetFromCache(link, referer, cancellationToken).ConfigureAwait(false))
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
                                info.Content.Seek(0, SeekOrigin.Begin);

                                res.Add(info);
                            }
                        }
                    }
                }
                catch
                {
                    var info = new ArchiveFileInfo 
                    { 
                        Name = resp.Info.FileName, 
                        Ext = resp.Info.FileName.GetPathExtension().ToLower(), 
                        Content = new MemoryStream() 
                    };
                    resp.Content.Seek(0, SeekOrigin.Begin);
                    resp.Content.CopyTo(info.Content);
                    info.Content.Seek(0, SeekOrigin.Begin);

                    res.Add(info);
                }

                if (res.Count > 0)
                    AddToCache(link, resp);
            }

            return res;
        }

        public async Task<Stream> GetStream(
            string link,
            string referer,
            Dictionary<string, string> post_params,
            CancellationToken cancellationToken,
            int maxRetry = DefaultMaxRetry
            )
        {
            Response resp = await Get(link, referer, post_params, cancellationToken, maxRetry);
            return resp.Content;
        }

        private void AddToCache(Link link, Response resp)
        {
            try
            {
                if (_cache == null || resp == null || resp.Cached) return;
                _cache.FromRegion(link.CacheRegion).Add<ResponseInfo>(link.CacheKey, resp.Content, resp.Info);
            }
            catch { }
        }

        private async Task<Response> GetFromCache(Link link, string referer, CancellationToken cancellationToken, int maxRetry = DefaultMaxRetry)
        {
            try
            {
                if (_cache != null)
                {
                    Response resp = new Response();
                    ResponseInfo respInfo;
                    resp.Content = _cache.FromRegion(link.CacheRegion).Get<ResponseInfo>(link.CacheKey, out respInfo);
                    if (resp.Content != null)
                    {
                        resp.Info = respInfo;
                        resp.Cached = true;
                        return resp;
                    }
                }
            }
            catch { }

            return await Get(link.Url, referer, link.PostParams, cancellationToken, maxRetry);
        }
    }
}
