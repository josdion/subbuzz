using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Configuration;
using subbuzz.Extensions;
using subbuzz.Helpers;
using subbuzz.Providers.PodnapisiAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Serialization;

namespace subbuzz.Providers
{
    class PodnapisiNet : ISubBuzzProvider
    {
        internal const string NAME = "Podnapisi.NET";
        private const string ServerUrl = "https://www.podnapisi.net";
        private static readonly string[] CacheRegionSub = { "podnapisi", "sub" };
        private static readonly string[] CacheRegionSearch = { "podnapisi", "search" };

        private readonly Logger _logger;
        private Http.Download _downloader;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public PodnapisiNet(
            Logger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;
            _downloader = new Http.Download(logger);

        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                return await _downloader.GetSubtitles(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"GetSubtitles error: {e}");
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var res = new List<SubtitleInfo>();

            try
            {
                if (!GetOptions().EnablePodnapisiNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0} S{1:D2}E{2:D2}");
                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (si.SearchText.IsNullOrWhiteSpace())
                {
                    return res;
                }

                var url = new StringBuilder($"{ServerUrl}/subtitles/search/old?sXML=1");
                url.Append($"&sL={si.Lang}");

                if (si.VideoType == VideoContentType.Episode)
                {
                    url.Append($"&sK={HttpUtility.UrlEncode(si.TitleSeries)}");
                    if (si.SeasonNumber != null) url.Append($"&sTS={si.SeasonNumber}");
                    if (si.EpisodeNumber != null) url.Append($"&sTE={si.EpisodeNumber}");
                }
                else
                {
                    url.Append($"&sK={HttpUtility.UrlEncode(si.SearchText)}");
                    if ((si.Year ?? 0) > 0) url.Append($"&sY={si.Year}");
                }

                res = await SearchUrl(url.ToString(), si, cancellationToken);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            try
            {
                int nextPage = 1;
                int maxPages = 0;

                do
                {
                    string urlWithPage = url;
                    if (nextPage > 1)
                        urlWithPage += $"&page={nextPage}";

                    var link = new Http.RequestCached
                    {
                        Url = urlWithPage,
                        Referer = ServerUrl,
                        Type = Http.RequestType.GET,
                        CacheRegion = CacheRegionSearch,
                        CacheLifespan = GetOptions().Cache.GetSearchLife(),
                    };

                    using (var resp = await _downloader.GetResponse(link, cancellationToken))
                    {
                        var xmlStream = resp.Content;
                        var settings = new XmlReaderSettings
                        {
                            ValidationType = ValidationType.None,
                            CheckCharacters = false,
                            IgnoreComments = true,
                            DtdProcessing = DtdProcessing.Parse,
                            Async = true
                        };

                        using (var xml = XmlReader.Create(xmlStream, settings))
                        {
                            XmlSerializer serializer = new XmlSerializer(typeof(Results));
                            var results = (Results)serializer.Deserialize(xml);
                            var subs = await ProcessSubtitles(results.subtitles, si, cancellationToken);
                            res.AddRange(subs);

                            nextPage = results.pages.current + 1;
                            maxPages = results.pages.count;
                        }

                        _downloader.AddResponseToCache(link, resp);
                    }
                }
                while (nextPage <= maxPages);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"GET: {url}: Search error: {e}");
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> ProcessSubtitles(PodnapisiAPI.Models.Subtitle[] subs, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();
            if (subs == null) return res;

            foreach (var sub in subs)
            {
                try
                {
                    List<SubtitleInfo> subsInfo = await ProcessSubtitle(sub, si, cancellationToken);
                    res.AddRange(subsInfo);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"GET: {sub.pid}: Search error: {e}");
                }
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> ProcessSubtitle(PodnapisiAPI.Models.Subtitle sub, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();
            if (sub == null) return res;

            if (sub.pid.IsNullOrWhiteSpace())
                return res;

            string subLink = $"{ServerUrl}/subtitles/{sub.pid}/download";

            if (sub.language.IsNotNullOrWhiteSpace())
                if (sub.language != si.LanguageInfo.TwoLetterISOLanguageName)
                    return res;

            string title = "...";
            if (sub.title.IsNotNullOrWhiteSpace())
                title = sub.title;

            if (sub.year.IsNotNullOrWhiteSpace())
                title += $" ({sub.year})";

            if (si.VideoType == VideoContentType.Episode)
            {
                if (sub.tvSeason > 0)
                {
                    if (sub.tvSeason != si.SeasonNumber)
                        return res;

                    title += string.Format(" S{0:D2}", sub.tvSeason);
                    if (sub.tvEpisode > 0)
                        title += string.Format("E{0:D2}", sub.tvEpisode);

                    if (sub.tvEpisode != 0 && sub.tvEpisode != si.EpisodeNumber)
                        return res;
                }
            }

            var subScoreBase = new SubtitleScore();
            si.MatchTitle(title, ref subScoreBase);
            si.MatchYear(sub.year, ref subScoreBase);

            string subDate = "";
            DateTime? dt = null;
            try
            {
                DateTimeOffset dto = DateTimeOffset.FromUnixTimeSeconds(sub.timestamp);
                dt = dto.LocalDateTime;
                subDate = dto.ToString("g", CultureInfo.CurrentCulture);
            }
            catch (Exception)
            {
            }

            string subInfo = title;
            if (sub.releases.releases != null)
            {
                foreach (var rel in sub.releases.releases)
                {
                    subInfo += $"<br>{rel}";
                }
            }

            if (sub.uploaderName.IsNullOrWhiteSpace()) sub.uploaderName = "Anonymous";
            subInfo += string.Format("<br>{0} | {1}", subDate, sub.uploaderName);

            var link = new Http.RequestSub
            {
                Url = subLink,
                Referer = ServerUrl,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionSub,
                CacheLifespan = GetOptions().Cache.GetSubLife(),
                Lang = si.GetLanguageTag(),
                FpsAsString = sub.fps,
                FpsVideo = si.VideoFps,
            };

            using (var files = await _downloader.GetArchiveFiles(link, cancellationToken).ConfigureAwait(false))
            {
                int subFilesCount = files.SubCount;
                foreach (var file in files)
                {
                    if (!file.IsSubfile())
                    {
                        _logger.LogInformation($"Ignore invalid subtitle file: {file.Name}");
                        continue;
                    }

                    link.File = file.Name;
                    link.Fps = file.Sub.FpsRequested;
                    link.IsForced = sub.new_flags?.flag?.ContainsIgnoreCase("foreign_only");
                    link.IsSdh = sub.new_flags?.flag?.ContainsIgnoreCase("hearing_impaired");

                    string subFpsInfo = link.Fps == null ? string.Empty : link.Fps?.ToString(CultureInfo.InvariantCulture);
                    if (file.Sub.FpsRequested != null && file.Sub.FpsDetected != null &&
                        Math.Abs(file.Sub.FpsRequested ?? 0 - file.Sub.FpsDetected ?? 0) > 0.001)
                    {
                        subFpsInfo = $"{file.Sub.FpsRequested?.ToString(CultureInfo.InvariantCulture)} ({file.Sub.FpsDetected?.ToString(CultureInfo.InvariantCulture)})";
                        link.Fps = file.Sub.FpsDetected;
                    }

                    SubtitleScore subScore = (SubtitleScore)subScoreBase.Clone();
                    si.MatchFps(link.Fps, ref subScore);

                    bool scoreVideoFileName = subFilesCount == 1 && subInfo.ContainsIgnoreCase(si.FileName);
                    bool ignorMutliDiscSubs = subFilesCount > 1;

                    float score = si.CaclScore(file.Name, subScore, scoreVideoFileName, ignorMutliDiscSubs);
                    if (score == 0 || score < Plugin.Instance.Configuration.MinScore)
                    {
                        _logger.LogInformation($"Ignore file: {file.Name} PID: {sub.pid} Score: {score}");
                        continue;
                    }

                    var subComment = subInfo;
                    if (subFpsInfo.IsNotNullOrWhiteSpace()) subComment += $" | {subFpsInfo}";
                    subComment += " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %";

                    var item = new SubtitleInfo
                    {
                        ProviderName = Name,
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = link.GetId(),
                        Name = file.Name,
                        PageLink = sub.url,
                        Format = file.GetExtSupportedByEmby(),
                        Author = sub.uploaderName,
                        Comment = subComment,
                        DownloadCount = sub.downloads,
                        CommunityRating = sub.rating,
                        DateCreated = dt,
                        IsHashMatch = false,
                        IsForced = link.IsForced,
                        IsSdh = link.IsSdh,
                        Score = score,
                    };

                    res.Add(item);
                }
            }

            return res;
        }

    }
}
