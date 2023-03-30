﻿using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Text;
using System.Globalization;

#if EMBY
using MediaBrowser.Common.Net;
#else
using System.Net.Http;
#endif

namespace subbuzz.Providers
{
    class YavkaNet : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "yavka.net";
        private const string ServerUrl = "https://yavka.net";
        private const string HttpReferer = "https://yavka.net/subtitles.php";
        private static readonly List<string> Languages = new List<string> { "bg", "en", "ru", "es", "it" };
        private static readonly string[] CacheRegionSub = { "yavka.net", "sub" };

        private readonly Logger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;
        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        protected class SearchResultItem
        {
            public string Link;
            public string Title;
            public string Year;
            public string Info;
            public string InfoBase;
            public string Fps;
            public string Uploader;
            public string Downloads;
        }

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public YavkaNet(
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
                if (!GetOptions().EnableYavkaNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, "{0} s{1:D2}e{2:D2}", "{0} s{1:D2}");
                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (!Languages.Contains(si.Lang) || String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                var searchTasks = new List<Task<List<SearchResultItem>>>();

                if (!String.IsNullOrWhiteSpace(si.ImdbId))
                {
                    // search by IMDB Id
                    string urlImdb = String.Format(
                        "{0}/subtitles.php?s={1}&y=&c=&u=&l={2}&g=&i={3}",
                        ServerUrl,
                        request.ContentType == VideoContentType.Episode ?
                            String.Format("s{0:D2}e{1:D2}", request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0) : "",
                        si.Lang.ToUpper(),
                        si.ImdbId
                        );

                    searchTasks.Add(SearchUrl(urlImdb, cancellationToken));
                }

                if (!String.IsNullOrWhiteSpace(si.SearchText))
                {
                    // search for movies/series by title
                    string url = String.Format(
                            "{0}/subtitles.php?s={1}&y={2}&c=&u=&l={3}&g=&i=",
                            ServerUrl,
                            HttpUtility.UrlEncode(si.SearchText),
                            request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "",
                            si.Lang.ToUpper()
                            );

                    searchTasks.Add(SearchUrl(url, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && !String.IsNullOrWhiteSpace(si.SearchSeason) && (si.SeasonNumber ?? 0) > 0)
                {
                    // search for episodes in season packs
                    string urlSeason = String.Format(
                            "{0}/subtitles.php?s={1}&y={2}&c=&u=&l={3}&g=&i=",
                            ServerUrl,
                            HttpUtility.UrlEncode(si.SearchSeason),
                            "",
                            si.Lang.ToUpper()
                            );

                    searchTasks.Add(SearchUrl(urlSeason, cancellationToken));
                }

                var sr = new List<SearchResultItem>();
                foreach (var task in searchTasks)
                {
                    List<SearchResultItem> srTmp = await task;
                    MergeSearchResultItems(sr, srTmp);
                }

                var processTasks = new List<Task<List<SubtitleInfo>>>();
                foreach (var sritem in sr)
                {
                    processTasks.Add(ProcessSearchResult(sritem, si, cancellationToken));
                }

                foreach (var task in processTasks)
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

        protected async Task<List<SearchResultItem>> SearchUrl(string url, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"GET: {url}");

                using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
                {
                    return ParseSearchResult(html);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"GET: {url}: Search error: {e}");
                return new List<SearchResultItem>();
            }
        }

        protected List<SearchResultItem> ParseSearchResult(System.IO.Stream html)
        {
            var res = new List<SearchResultItem>();

            var config = AngleSharp.Configuration.Default;
            var context = AngleSharp.BrowsingContext.New(config);
            var parser = new AngleSharp.Html.Parser.HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var trNodes = htmlDoc.GetElementsByTagName("tr");
            foreach (var tr in trNodes)
            {
                var sritem = new SearchResultItem();

                var tds = tr.GetElementsByTagName("td");
                if (tds == null || tds.Count() < 1) continue;
                var td = tds[0];

                var link = td.QuerySelector("a[class='balon']");
                if (link == null)
                {
                    link = td.QuerySelector("a[class='selector']");
                    if (link == null) continue;
                }

                sritem.Link = ServerUrl + "/" + link.GetAttribute("href").TrimStart('/');
                sritem.Title = link.InnerHtml;

                sritem.Year = "";
                if (link.NextSibling != null)
                    sritem.Year = link.NextElementSibling.TextContent.Trim(new[] { ' ', '(', ')' });

                if (!String.IsNullOrWhiteSpace(sritem.Year))
                    sritem.Title += $" ({sritem.Year})";

                string subNotes = link.GetAttribute("content");
                var regex = new Regex(@"(?s)<p.*><img [A-z0-9=\'/\. :;#-]*>(.*)</p>");
                sritem.InfoBase = regex.Replace(subNotes, "$1");
                sritem.Info = Utils.TrimString(sritem.InfoBase, "<br />");
                sritem.Info = sritem.Info.Replace("<br /><br />", "<br />").Replace("<br /><br />", "<br />");

                sritem.Fps = "";
                var fps = td.QuerySelector("span[title='Кадри в секунда']");
                if (fps != null)
                    sritem.Fps = fps.TextContent.Trim();

                sritem.Uploader = "";
                var upl = td.QuerySelector("a[class='click']");
                if (upl != null)
                    sritem.Uploader = upl.TextContent.Trim();

                sritem.Downloads = "0";
                var downlds = td.QuerySelector("div > strong");
                if (downlds != null)
                    sritem.Downloads = downlds.TextContent.Trim();

                res.Add(sritem);
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> ProcessSearchResult(SearchResultItem sritem, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var subPageInfo = await GetSubInfoPage(sritem.Link, cancellationToken);
            if (subPageInfo == null ||
                !subPageInfo.ContainsKey("action") ||
                !subPageInfo.ContainsKey("id") ||
                !subPageInfo.ContainsKey("lng"))
            {
                _logger.LogInformation($"Invalid information from subtitle page: {sritem.Link}");
                return res;
            }

            var subScoreBase = new SubtitleScore();
            si.MatchTitle(sritem.Title, ref subScoreBase);

            string subLink = subPageInfo["action"];
            string subInfo = sritem.Title + (string.IsNullOrWhiteSpace(sritem.Info) ? "" : "<br>" + sritem.Info);

            Download.LinkSub link = new Download.LinkSub
            {
                Url = subLink,
                PostParams = new Dictionary<string, string> { { "id", subPageInfo["id"] }, { "lng", subPageInfo["lng"] } },
                CacheKey = sritem.Link,
                CacheRegion = CacheRegionSub,
                Lang = si.LanguageInfo.TwoLetterISOLanguageName,
                Fps = Download.LinkSub.FpsFromStr(sritem.Fps),
                FpsVideo = si.VideoFps,
            };

            using (var files = await downloader.GetArchiveFiles(link, sritem.Link, cancellationToken).ConfigureAwait(false))
            {
                int imdbId = 0;
                string subImdb = "";
                DateTime? dt = null;
                DateTimeOffset? dtOffset = null;

                foreach (var fitem in files)
                {
                    if (fitem.Name == "YavkA.net.txt")
                    {
                        fitem.Content.Seek(0, System.IO.SeekOrigin.Begin);
                        var reader = new System.IO.StreamReader(fitem.Content, Encoding.UTF8, true);
                        string info_text = reader.ReadToEnd();
                        var regexDate = new Regex(@"Качени на: (\d\d\d\d-\d\d-\d\d \d\d:\d\d:\d\d)");
                        var match = regexDate.Match(info_text);
                        if (match.Success && match.Groups.Count > 0)
                        {
                            dt = DateTime.Parse(match.Groups[1].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                            dtOffset = DateTimeOffset.Parse(match.Groups[1].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                        }

                        var regexImdbId = new Regex(@"iMDB ID: (tt(\d+))");
                        match = regexImdbId.Match(info_text);
                        if (match.Success && match.Groups.Count > 2)
                        {
                            subImdb = match.Groups[1].ToString();
                            imdbId = int.Parse(match.Groups[2].ToString());
                        }

                        break;
                    }
                }

                if (!si.MatchImdbId(imdbId, ref subScoreBase))
                {
                    //_logger.LogInformation($"Ignore result {subImdb} {subTitle} not matching IMDB ID");
                    //continue;
                }

                string subDate = dtOffset != null ? dtOffset?.ToString("g", CultureInfo.CurrentCulture) : "";
                subInfo += string.Format("<br>{0} | {1}", subDate, sritem.Uploader);

                var subFilesCount = files.CountSubFiles();

                foreach (var file in files)
                {
                    if (!file.IsSubfile()) continue;

                    link.File = file.Name;
                    link.Fps = file.Sub.FpsRequested;

                    string subFpsInfo = sritem.Fps;
                    if (file.Sub.FpsRequested != null && file.Sub.FpsDetected != null &&
                        Math.Abs(file.Sub.FpsRequested ?? 0 - file.Sub.FpsDetected ?? 0) > 0.001)
                    {
                        subFpsInfo = $"{file.Sub.FpsRequested?.ToString(CultureInfo.InvariantCulture)} ({file.Sub.FpsDetected?.ToString(CultureInfo.InvariantCulture)})";
                        link.Fps = file.Sub.FpsDetected;
                    }

                    SubtitleScore subScore = (SubtitleScore)subScoreBase.Clone();
                    si.MatchFps(link.Fps, ref subScore);

                    float score = si.CaclScore(file.Name, subScore, subFilesCount == 1 && sritem.InfoBase.ContainsIgnoreCase(si.FileName));

                    if (score == 0 || score < GetOptions().MinScore)
                    {
                        _logger.LogInformation($"Ignore file: {file} Score: {score}");
                        continue;
                    }

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = link.GetId(),
                        ProviderName = Name,
                        Name = $"<a href='{sritem.Link}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file.Name}</a>",
                        Format = file.GetExtSupportedByEmby(),
                        Author = sritem.Uploader,
                        Comment = subInfo + " | " + subFpsInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        //CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        DownloadCount = int.Parse(sritem.Downloads),
                        IsHashMatch = score >= GetOptions().HashMatchByScore,
                        IsForced = false,
                        Score = score,
#if EMBY
                    DateCreated = dtOffset,
#else
                        DateCreated = dt,
#endif
                    };

                    res.Add(item);
                }
            }

            if (res.Count > 0)
            {
                // TODO: Add subtitle info page to cache
            }

            return res;
        }

        protected async Task<Dictionary<string, string>> GetSubInfoPage(string url, CancellationToken cancellationToken)
        {
            var res = new Dictionary<string, string>();

            using (var html = await downloader.GetStream(url, HttpReferer, null, cancellationToken))
            {
                var config = AngleSharp.Configuration.Default;
                var context = AngleSharp.BrowsingContext.New(config);
                var parser = new AngleSharp.Html.Parser.HtmlParser(context);
                var htmlDoc = parser.ParseDocument(html);

                var formNodes = htmlDoc.GetElementsByTagName("form");
                foreach (var form in formNodes)
                {
                    var id = form.QuerySelector("input[name='id']");
                    if (id == null) continue;

                    var lng = form.QuerySelector("input[name='lng']");
                    if (lng == null) continue;

                    res["action"] = form.GetAttribute("action");
                    res["id"] = id.GetAttribute("value");
                    res["lng"] = lng.GetAttribute("value");
                    return res;
                }

                return res;
            }
        }

        protected static void MergeSearchResultItems(List<SearchResultItem> res, List<SearchResultItem> sub)
        {
            foreach (var s in sub)
            {
                bool add = true;

                foreach (var r in res)
                {
                    if (s.Link == r.Link)
                    {
                        add = false;
                        break;
                    }
                }

                if (add)
                    res.Add(s);
            }
        }

    }
}
