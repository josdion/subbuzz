using MediaBrowser.Controller.Subtitles;
using SharpCompress.Archives;
using subbuzz.Configuration;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers.Http
{
    public class Download : Client
    {
        private const string _defaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/112.0";
        private static PluginConfiguration GetOptions()
            => Plugin.Instance?.Configuration;

        private static FileCache GetCache(string[] region, int life)
            => Plugin.Instance?.Cache?.FromRegion(region, life);

        public Download(Logger logger) : base(logger)
        {
            AddDefaultRequestHeader("Pragma", "no-cache");
            AddDefaultRequestHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            AddDefaultRequestHeader("Accept-Language", "en-US,en;q=0.7,bg;q=0.3");
            AddDefaultRequestHeader("Upgrade-Insecure-Requests", "1");
            AddDefaultRequestHeader("User-Agent", _defaultUserAgent);
            Timeout = 20;
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            var link = RequestSub.FromId(id);
            using (var files = await GetArchiveFiles(link, cancellationToken))
            {
                foreach (File file in files)
                {
                    if (link.File.IsNullOrWhiteSpace() || link.File == file.Name)
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
                            IsForced = link.IsForced ?? false,
                            Stream = fileStream
                        };
                    }
                }
            }

            throw new Exception($"File not found! {id}");
        }

        public async Task<FileList> GetArchiveFiles(RequestSub link, CancellationToken cancellationToken)
        {
            var res = new FileList();

            using (Response resp = await GetResponseForSubtitles(link, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    res.AddRange(ReadArchive(resp.Content));
                }
                catch
                {
                    var info = new File
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
                    f.Sub = f.Content.Length < (1024 * 1024) ? Subtitle.Load(f.Content, GetOptions().SubEncoding, link.Fps) : null;
                }

                if (res.SubCount > 0)
                    AddSubtitlesToCache(link, resp);
            }

            return res;
        }

        public async Task<Response> GetResponse(RequestCached link, CancellationToken cancellationToken, int? retryIfExpiredFound = null)
        {
            bool expiredFound = false;

            try
            {
                _logger.LogDebug($"{link}");
                if (link.CacheLifespan >= 0 && link.CacheRegion != null)
                {
                    Response resp = new Response { Cached = true };
                    resp.Content = GetCache(link.CacheRegion, link.CacheLifespan).Get(link.CacheKey ?? link.Url, out ResponseInfo respInfo);
                    resp.Info = respInfo;

                    _logger.LogDebug($"Response loaded from cache [{string.Join("/", link.CacheRegion)}], Url: {link.Url}, Key: {link.CacheKey ?? "<null>"}");
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
                _logger.LogError(e, $"Unable to get response from cache: {e}");
            }

            try
            {
                return await SendFormAsync(link, expiredFound ? retryIfExpiredFound : null, cancellationToken);
            }
            catch (Exception e)
            {
                if (!expiredFound) throw;

                _logger.LogError(e, $"Get response from cache, as it's not able to get the response from server: {e}");

                Response resp = new Response { Cached = true };
                resp.Content = GetCache(link.CacheRegion, -1).Get(link.CacheKey ?? link.Url, out ResponseInfo respInfo);
                resp.Info = respInfo;

                _logger.LogInformation($"Response loaded from cache [{string.Join("/", link.CacheRegion)}], Url: {link.Url}, Key: {link.CacheKey ?? "<null>"}");
                return resp;
            }
        }

        public void AddResponseToCache(RequestCached link, Response resp)
        {
            try
            {
                if (link.CacheLifespan < 0 || link.CacheRegion == null || resp.Cached) return;
                GetCache(link.CacheRegion, link.CacheLifespan).Add(link.CacheKey ?? link.Url, resp.Content, resp.Info);
                _logger.LogDebug($"Add response to cache [{string.Join("/", link.CacheRegion)}], Url: {link.Url}, Key: {link.CacheKey ?? "<null>"}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Can't add to cache: {e}");
            }
        }

        private FileList ReadArchive(Stream content, string baseKey = null)
        {
            var res = new FileList();

            using (IArchive arcreader = ArchiveFactory.Open(content))
            {
                foreach (IArchiveEntry entry in arcreader.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        var info = new File
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

        private void AddSubtitlesToCache(RequestSub link, Response resp)
        {
            link.CacheLifespan = GetOptions().Cache.GetSubLife();
            AddResponseToCache(link, resp);
        }

        private async Task<Response> GetResponseForSubtitles(RequestSub link, CancellationToken cancellationToken)
        {
            link.CacheLifespan = GetOptions().Cache.GetSubLife();
            return await GetResponse(link, cancellationToken, 0);
        }

    }
}
