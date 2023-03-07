using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Helpers;
using subbuzz.Extensions;
using subbuzz.Providers.PodnapisiAPI.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Serialization;
using System.Globalization;

#if EMBY
using MediaBrowser.Common.Net;
using ILogger = MediaBrowser.Model.Logging.ILogger;
#else
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger<subbuzz.Providers.SubBuzz>;
#endif

#if JELLYFIN
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    class PodnapisiNet : ISubBuzzProvider
    {
        internal const string NAME = "Podnapisi.NET";
        private const string ServerUrl = "https://www.podnapisi.net";

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public PodnapisiNet(
            ILogger logger,
            IFileSystem fileSystem,
            ILocalizationManager localizationManager,
            ILibraryManager libraryManager,
#if JELLYFIN
            IHttpClientFactory http
#else
            IHttpClient http
#endif
            )
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;
            downloader = new Download(http, Plugin.Instance.Cache?.FromRegion(NAME));

        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                var postProcessing = Plugin.Instance.Configuration.SubPostProcessing;
                postProcessing.EncodeSubtitlesToUTF8 = true;

                return await downloader.GetArchiveSubFile(
                    id, 
                    ServerUrl, 
                    Encoding.GetEncoding(1251),
                    postProcessing,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GetSubtitles error: {e}");
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
                if (!Plugin.Instance.Configuration.EnablePodnapisiNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0} S{1:D2}E{2:D2}");
                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

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
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"{NAME}: Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

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

                    _logger.LogInformation($"{NAME}: GET: {urlWithPage}");

                    using (var xmlStream = await downloader.GetStream(urlWithPage, ServerUrl, null, cancellationToken))
                    {
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
                    }
                }
                while (nextPage <= maxPages);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: Search error: {e}");
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
                    _logger.LogError(e, $"{NAME}: GET: {sub.pid}: Search error: {e}");
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

            subInfo += string.Format("<br>{0} | {1}", subDate, sub.uploaderName);

            string subFps = si.VideoFps.ToString();
            if (sub.fps.IsNotNullOrWhiteSpace() && sub.fps != "N/A")
            {
                subFps = sub.fps;
                si.MatchFps(subFps, ref subScoreBase);
                subInfo += $" | {subFps}";
            }

            var files = await downloader.GetArchiveFileNames(subLink, ServerUrl, cancellationToken).ConfigureAwait(false);

            foreach (var (fileName, fileExt) in files)
            {
                bool scoreVideoFileName = files.Count == 1 && subInfo.ContainsIgnoreCase(si.FileName);
                bool ignorMutliDiscSubs = files.Count > 1;

                float score = si.CaclScore(fileName, subScoreBase, scoreVideoFileName, ignorMutliDiscSubs);
                if (score == 0 || score < Plugin.Instance.Configuration.MinScore)
                {
                    _logger.LogInformation($"{NAME}: Ignore file: {fileName} PID: {sub.pid}");
                    continue;
                }

                var item = new SubtitleInfo
                {
                    ProviderName = Name,
                    ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                    Id = Download.GetId(subLink, fileName, si.LanguageInfo.TwoLetterISOLanguageName, subFps),
                    Name = $"<a href='{sub.url}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{fileName}</a>",
                    Format = "srt",
                    Author = sub.uploaderName,
                    Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                    DownloadCount = sub.downloads,
                    CommunityRating = sub.rating,
                    DateCreated = dt,
                    IsHashMatch = false,
                    IsForced = false,
                    Score = score,
                };

                res.Add(item);
            }

            return res;
        }

    }
}
