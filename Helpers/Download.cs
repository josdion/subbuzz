using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives;
using subbuzz.Extensions;
using SubtitlesParser.Classes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

#if EMBY
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubBuzz>;
#endif

namespace subbuzz.Helpers
{
    public partial class Download
    {
        private const int DefaultMaxRetry = 5;
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0";

        private readonly ILogger _logger;
        private string _providerName = "";

        private PluginConfiguration GetOptions()
            => Plugin.Instance?.Configuration;

        private FileCache GetCache(string[] region, int life = 0)
            => Plugin.Instance?.Cache?.FromRegion(region, life);

        public class Link
        {
            public string Url { get; set; } 
            public Dictionary<string, string> PostParams { get; set; }
            public string CacheKey { get; set; } = null;
            public string[] CacheRegion { get; set; }

            [JsonIgnore]
            public int CacheLifespan { get; set; }
        }

        public class LinkSub : Link 
        {
            public string File { get; set; } = string.Empty;
            public string Lang { get; set; } = string.Empty;
            public float? Fps { get; set; } = null;
            public float? FpsVideo { get; set; } = null;


            public string GetId()
            {
                return Utils.Base64UrlEncode<LinkSub>(this);
            }

            public static LinkSub FromId(string id)
            {
                if (id.IsNotNullOrWhiteSpace())
                    return Utils.Base64UrlDecode<LinkSub>(id);

                return default;
            }

            public static float? FpsFromStr(string fps)
            {
                try 
                { 
                    var f = float.Parse(fps, CultureInfo.InvariantCulture);
                    return f < 1 ? null : f;
                } 
                catch 
                {
                    return null; 
                }
            }
        }

        public class ArchiveFileInfo : IDisposable
        {
            public string Name { get; set; }
            public string Ext { get; set; }
            public Stream Content { get; set; }
            public Subtitle Sub { get; set; } = null;
            public void Dispose() => Content?.Dispose();

            public bool IsSubfile()
            {
                return Sub == null ? false : SubtitlesFormat.IsFormatSupported(Sub.Format);
            }

            public string GetExtSupportedByEmby()
            {
                if (Sub ==  null) return null;
                return SubtitleConvert.GetExtSupportedByEmby(Sub.Format);
            }

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

            public int CountSubFiles()
            {
                int count = 0;
                foreach (var item in this)
                {
                    if (item.IsSubfile()) count++;
                }
                return count;
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

        public async Task<SubtitleResponse> GetSubtitles(string id, string referer, CancellationToken cancellationToken)
        {
            LinkSub link = LinkSub.FromId(id);
            using (ArchiveFileInfoList files = await GetArchiveFiles(link, referer, cancellationToken))
            {
                foreach (ArchiveFileInfo file in files)
                {
                    if (string.IsNullOrWhiteSpace(link.File) || link.File == file.Name)
                    {
                        string format;
                        Stream fileStream = SubtitleConvert.ToSupportedFormat(
                            file.Content, link.Fps, out format,
                            GetOptions().SubEncoding, GetOptions().SubPostProcessing,
                            file.Sub);

                        return new SubtitleResponse
                        {
                            Language = link.Lang,
                            Format = format,
                            IsForced = false,
                            Stream = fileStream
                        };
                    }
                }
            }

            return new SubtitleResponse();
        }

        public async Task<ArchiveFileInfoList> GetArchiveFiles(LinkSub link, string referer, CancellationToken cancellationToken)
        {
            var res = new ArchiveFileInfoList();

            using (Response resp = await GetResponseForSubtitles(link, referer, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    res.AddRange(ReadArchive(resp.Content));
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

                foreach (var f in res)
                {
                    f.Sub = f.Content.Length < (1024*1024) ? Subtitle.Load(f.Content, GetOptions().SubEncoding, link.Fps) : null;
                }

                if (res.CountSubFiles() > 0)
                    AddSubtitlesToCache(link, resp);
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

        private ArchiveFileInfoList ReadArchive(Stream content, string baseKey = null)
        {
            var res = new ArchiveFileInfoList();

            using (IArchive arcreader = ArchiveFactory.Open(content))
            {
                foreach (IArchiveEntry entry in arcreader.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        var info = new ArchiveFileInfo
                        {
                            Name = string.IsNullOrWhiteSpace(baseKey) ? entry.Key : Path.Combine(baseKey, entry.Key),
                            Ext = entry.Key.GetPathExtension().ToLower(),
                            Content = new MemoryStream()
                        };

                        Stream arcStream = entry.OpenEntryStream();
                        arcStream.CopyTo(info.Content);
                        info.Content.Seek(0, SeekOrigin.Begin);

                        try
                        {
                            // try to extract internal archives
                            res.AddRange(ReadArchive(info.Content, info.Name));
                            info.Dispose();
                        }
                        catch
                        {
                            info.Content.Seek(0, SeekOrigin.Begin);
                            res.Add(info);
                        }

                    }
                }
            }

            return res;
        }

        private void AddSubtitlesToCache(Link link, Response resp)
        {
            Link tmpLink = new Link
            {
                Url = link.Url,
                PostParams = link.PostParams,
                CacheKey = link.CacheKey,
                CacheRegion = link.CacheRegion,
                CacheLifespan = GetOptions().Cache.Subtitle ? link.CacheLifespan : -1,
            };

            AddResponseToCache(tmpLink, resp);
        }

        private async Task<Response> GetResponseForSubtitles(Link link, string referer, CancellationToken cancellationToken, int maxRetry = DefaultMaxRetry)
        {
            Link tmpLink = new Link
            {
                Url = link.Url,
                PostParams = link.PostParams,
                CacheKey = link.CacheKey,
                CacheRegion = link.CacheRegion,
                CacheLifespan = GetOptions().Cache.Subtitle ? link.CacheLifespan : -1,
            };

            return await GetResponse(tmpLink, referer, cancellationToken, maxRetry);
        }

        public async Task<Response> GetResponse(Link link, string referer, CancellationToken cancellationToken, int maxRetry = DefaultMaxRetry)
        {
            bool expiredFound = false;

            try
            {
                if (link.CacheLifespan >= 0 && link.CacheRegion != null)
                {
                    Response resp = new Response { Cached = true };
                    resp.Content = GetCache(link.CacheRegion, link.CacheLifespan).Get(link.CacheKey ?? link.Url, out ResponseInfo respInfo);
                    resp.Info = respInfo;
                    return resp;
                }
            }
            catch (FileCacheItemNotFoundException)
            {
            }
            catch (FileCacheItemExpiredException)
            {
                expiredFound = true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{_providerName}: Unable to get response from cache: {e}");
            }

            try
            {
                return await Get(link.Url, referer, link.PostParams, cancellationToken, maxRetry);
            }
            catch (Exception e)
            {
                if (!expiredFound) throw;

                _logger.LogError(e, $"{_providerName}: Get response from cache, as it's not able to get the response from server: {e}");

                Response resp = new Response { Cached = true };
                resp.Content = GetCache(link.CacheRegion, 0).Get(link.CacheKey ?? link.Url, out ResponseInfo respInfo);
                resp.Info = respInfo;

                return resp;
            }
        }

        public void AddResponseToCache(Link link, Response resp)
        {
            try
            {
                if (link.CacheLifespan < 0 || link.CacheRegion == null || resp.Cached) return;
                GetCache(link.CacheRegion, link.CacheLifespan).Add(link.CacheKey ?? link.Url, resp.Content, resp.Info);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{_providerName}: Can't add to cache: {e}");
            }
        }

    }
}
