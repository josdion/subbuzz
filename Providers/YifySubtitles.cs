using AngleSharp;
using AngleSharp.Html.Parser;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Configuration;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers
{
    class YifySubtitles : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "YIFY Subtitles";
        private const string ServerUrl = "https://yifysubtitles.ch";
        private static readonly string[] CacheRegionSub = { "yifysubtitles", "sub" };
        private static readonly string[] CacheRegionSearch = { "yifysubtitles", "search" };

        private readonly Logger _logger;
        private Http.Download _downloader;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Movie };

        public int Order => 0;

        private static readonly Dictionary<string, string> LangMap = new Dictionary<string, string>
        {
            { "Brazilian Portuguese",   "pob" },
            { "Chinese BG code",        "chs" },
            { "Big 5 code",             "cht" },
            { "Chinese",                "chi" },
        };

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public YifySubtitles(
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
                if (!GetOptions().EnableYifySubtitles)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager);
                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                var tasks = new List<Task<List<SubtitleInfo>>>();

                if (si.ImdbId.IsNotNullOrWhiteSpace())
                {
                    // search by IMDB Id
                    string urlImdb = string.Format($"{ServerUrl}/movie-imdb/{si.ImdbId}");
                    tasks.Add(SearchUrl(urlImdb, si, true, cancellationToken));
                }
                else
                {
                    // TODO: url = $"{ServerUrl}/search?q={HttpUtility.UrlEncode(si.SearchText)};
                    _logger.LogInformation($"IMDB ID missing");
                    return res;
                }

                foreach (var task in tasks)
                {
                    List<SubtitleInfo> subs = await task;
                    Utils.MergeSubtitleInfo(res, subs);
                }

                //res.Sort((x, y) => y.Score.CompareTo(x.Score));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, SearchInfo si, bool byImdb, CancellationToken cancellationToken)
        {
            try
            {
                var link = new Http.RequestCached
                {
                    Url = url,
                    Referer = ServerUrl,
                    Type = Http.RequestType.GET,
                    CacheRegion = CacheRegionSearch,
                    CacheLifespan = GetOptions().Cache.GetSearchLife(),
                };

                using (var resp = await _downloader.GetResponse(link, cancellationToken))
                {
                    var res = await ParseHtml(resp.Content, si, byImdb, cancellationToken);
                    _downloader.AddResponseToCache(link, resp);
                    return res;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"GET: {url}: Search error: {e}");
                return new List<SubtitleInfo>();
            }
        }

        protected async Task<List<SubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, bool byImdb, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var config = AngleSharp.Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var tagTitles = htmlDoc.GetElementsByClassName("movie-main-title");
            if (tagTitles == null || tagTitles.Length != 1)
            {
                throw new Exception($"Invalid HTML. Can't find element with class=movie-main-title");
            }

            string subTitle = tagTitles[0].TextContent;

            var tbl = htmlDoc.QuerySelector("table.other-subs > tbody");
            var trs = tbl?.GetElementsByTagName("tr");
            if (trs == null)
            {
                _logger.LogInformation($"Invalid HTML");
                return res;
            }

            foreach (var tr in trs)
            {
                var tds = tr.GetElementsByTagName("td");
                if (tds == null || tds.Count() < 5) continue;

                string subRating = tds[0].TextContent;
                string subUploader = tds[4].TextContent;

                bool sdh = tds[3].QuerySelector("span.hi-subtitle") != null;

                string lang = tds[1].TextContent;
                if (LangMap.ContainsKey(lang))
                    lang = LangMap[lang];

                if (!si.IsRequestedLanguage(lang))
                    continue; // Ignore language

                var linkTag = tds[2].GetElementsByTagName("a")[0];
                string subLinkPage = linkTag.GetAttribute("href");
                var regexLink = new Regex(@"/subtitles/");
                string subLink;
                if (subLinkPage.Contains("://"))
                    subLink = regexLink.Replace(subLinkPage, "/subtitle/", 1) + ".zip";
                else
                    subLink = regexLink.Replace(subLinkPage, ServerUrl + "/subtitle/", 1) + ".zip";

                string subInfoBase = linkTag.InnerHtml;
                var regexInfo = new Regex(@"<span.*/span>");
                subInfoBase = regexInfo.Replace(subInfoBase, "").Trim();
                string subInfo = subTitle + (subInfoBase.IsNullOrWhiteSpace() ? "" : "<br>" + subInfoBase);
                subInfo += string.Format("<br>{0}", subUploader);

                var link = new Http.RequestSub
                {
                    Url = subLink,
                    Referer = ServerUrl,
                    Type = Http.RequestType.GET,
                    CacheRegion = CacheRegionSub,
                    CacheLifespan = GetOptions().Cache.GetSubLife(),
                    Lang = si.LanguageInfo.TwoLetterISOLanguageName,
                    FpsVideo = si.VideoFps,
                };

                using (var files = await _downloader.GetArchiveFiles(link, cancellationToken).ConfigureAwait(false))
                {
                    int subFilesCount = files.SubCount;

                    foreach (var file in files)
                    {
                        if (!file.IsSubfile()) continue;

                        SubtitleScore subScore = new SubtitleScore();
                        if (byImdb) subScore.AddMatch("imdb");

                        float score = si.CaclScore(file.Name, subScore, subFilesCount == 1 && subInfoBase.ContainsIgnoreCase(si.FileName));

                        link.File = file.Name;

                        var item = new SubtitleInfo
                        {
                            ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                            Id = link.GetId(),
                            ProviderName = Name,
                            Name = file.Name,
                            PageLink = ServerUrl + subLinkPage,
                            Format = file.GetExtSupportedByEmby(),
                            Author = subUploader,
                            Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            //DateCreated = DateTimeOffset.Parse(subDate),
                            CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                            //DownloadCount = int.Parse(subDownloads),
                            IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                            IsForced = null,
                            Sdh = sdh,
                            Score = score,
                        };

                        res.Add(item);
                    }
                }
            }

            return res;
        }

    }
}
