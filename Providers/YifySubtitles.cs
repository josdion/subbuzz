﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Html.Parser;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;

#if EMBY
using MediaBrowser.Common.Net;
#else
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    class YifySubtitles : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "YIFY Subtitles";
        private const string ServerUrl = "https://yifysubtitles.org";
        private const string HttpReferer = "https://yifysubtitles.org/";
        private static readonly string[] CacheRegionSub = { "yifysubtitles", "sub" };

        private readonly Logger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;
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

        public YifySubtitles(
            Logger logger,
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
            downloader = new Download(http, logger);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                return await downloader.GetSubtitles(id, HttpReferer, cancellationToken).ConfigureAwait(false);
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
                if (!Plugin.Instance.Configuration.EnableYifySubtitles)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager);
                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                var tasks = new List<Task<List<SubtitleInfo>>>();

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    // search by IMDB Id
                    string urlImdb = String.Format($"{ServerUrl}/movie-imdb/{si.ImdbId}");
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
                _logger.LogInformation($"GET: {url}");

                using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
                {
                    return await ParseHtml(html, si, byImdb, cancellationToken);
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

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var tagTitle = htmlDoc.GetElementsByClassName("movie-main-title").FirstOrDefault();
            if (tagTitle == null)
            {
                _logger.LogInformation($"Invalid HTML. Can't find element with class=movie-main-title");
                return res;
            }

            string subTitle = tagTitle.TextContent;

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
                string subInfo = subTitle + (String.IsNullOrWhiteSpace(subInfoBase) ? "" : "<br>" + subInfoBase);
                subInfo += string.Format("<br>{0}", subUploader);

                Download.LinkSub link = new Download.LinkSub
                {
                    Url = subLink,
                    CacheKey = subLink,
                    CacheRegion = CacheRegionSub,
                    Lang = si.LanguageInfo.TwoLetterISOLanguageName,
                    FpsVideo = si.VideoFps,
                };

                using (var files = await downloader.GetArchiveFiles(link, HttpReferer, cancellationToken).ConfigureAwait(false))
                {
                    int subFilesCount = files.CountSubFiles();

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
                            Name = $"<a href='{ServerUrl}{subLinkPage}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file.Name}</a>",
                            Format = file.GetExtSupportedByEmby(),
                            Author = subUploader,
                            Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            //DateCreated = DateTimeOffset.Parse(subDate),
                            CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                            //DownloadCount = int.Parse(subDownloads),
                            IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                            IsForced = false,
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
