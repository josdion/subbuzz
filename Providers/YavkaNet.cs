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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace subbuzz.Providers
{
    class YavkaNet : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "yavka.net";
        private const string ServerUrl = "https://yavka.net";
        private const string HttpReferer = "https://yavka.net/subtitles/";
        private static readonly List<string> Languages = new List<string> { "bg", "en", "ru", "es", "it" };
        private static readonly string[] CacheRegionSub = { "yavka.net", "sub" };
        private static readonly string[] CacheRegionSearch = { "yavka.net", "search" };

        private readonly Logger _logger;
        private readonly Http.Download _downloader;

        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
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
            public int ImdbId = 0;
            public DateTime? UploadedAt = null;
            public DateTime? UpdatedAt = null;
        }

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public YavkaNet(
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
            return await _downloader.GetSubtitles(id, cancellationToken).ConfigureAwait(false);
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

                if (!Languages.Contains(si.Lang) || si.SearchText.IsNullOrWhiteSpace())
                {
                    return res;
                }

                var searchTasks = new List<Task<List<SearchResultItem>>>();

                // NOTE: currently the IMDB search ignore language and return results only in Bulgarian
                if (si.ImdbId.IsNotNullOrWhiteSpace() && si.Lang == "bg")
                {
                    // search by IMDB Id
                    var postParams = GetPostParams(
                        request.ContentType == VideoContentType.Episode ?
                            string.Format("s{0:D2}e{1:D2}", request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0) : "",
                        "", 
                        si.Lang.ToUpper(), 
                        si.ImdbId);
                    
                    searchTasks.Add(SearchUrl($"{ServerUrl}/search", postParams, cancellationToken));
                }

                if (si.SearchText.IsNotNullOrWhiteSpace())
                {
                    // search for movies/series by title
                    var postParams = GetPostParams(
                        si.SearchText,
                        request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "",
                        si.Lang.ToUpper(),
                        "");

                    searchTasks.Add(SearchUrl($"{ServerUrl}/search", postParams, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && si.SearchSeason.IsNotNullOrWhiteSpace() && (si.SeasonNumber ?? 0) > 0)
                {
                    // search for episodes in season packs
                    var postParams = GetPostParams(
                        si.SearchSeason,
                        "",
                        si.Lang.ToUpper(),
                        "");

                    searchTasks.Add(SearchUrl($"{ServerUrl}/search", postParams, cancellationToken));
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

        protected Dictionary<string, string> GetPostParams(string query, string year, string lang, string imdb)
        {
            return new Dictionary<string, string>
                {
                    { "s", query }, // search text
                    { "y", year },  // year
                    { "c", "" },    // season
                    { "u", "" },    // uploader
                    { "l", lang },  // language
                    { "g", "" },    // genre
                    { "i", imdb },  // iMDB ID ttXXXXX
                    { "search", "\uf002+Търсене" },
                };
        }

        protected async Task<List<SearchResultItem>> SearchUrl(string url, Dictionary<string, string> post_params, CancellationToken cancellationToken)
        {
            try
            {
                var link = new Http.RequestCached
                {
                    Url = url,
                    Referer = HttpReferer,
                    Type = post_params == null ? Http.RequestType.GET : Http.RequestType.POST,
                    Params = post_params,
                    CacheKey = post_params == null ? null : url + $":post={{{string.Join(",", post_params)}}}",
                    CacheRegion = CacheRegionSearch,
                    CacheLifespan = GetOptions().Cache.GetSearchLife(),
                };

                using (var resp = await _downloader.GetResponse(link, cancellationToken))
                {
                    var res = ParseSearchResult(resp.Content);
                    _downloader.AddResponseToCache(link, resp);
                    return res;
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

                if (sritem.Year.IsNotNullOrWhiteSpace())
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

            (var action, var postParams, sritem) = await GetSubInfoPage(sritem, cancellationToken);
            if (action.IsNullOrWhiteSpace() || postParams.IsNullOrEmpty())
            {
                _logger.LogInformation($"Invalid information from subtitle page: {sritem.Link}");
                return res;
            }

            var subScoreBase = new SubtitleScore();
            si.MatchTitle(sritem.Title, ref subScoreBase);

            string subLink = action;
            string subInfo = sritem.Title + (string.IsNullOrWhiteSpace(sritem.Info) ? "" : "<br>" + sritem.Info);

            var link = new Http.RequestSub
            {
                Url = subLink,
                Referer = sritem.Link,
                Type = Http.RequestType.POST,
                Params = postParams,
                CacheRegion = CacheRegionSub,
                CacheLifespan = GetOptions().Cache.GetSubLife(),
                Lang = si.GetLanguageTag(),
                FpsAsString = sritem.Fps,
                FpsVideo = si.VideoFps,
            };

            using (var files = await _downloader.GetArchiveFiles(link, cancellationToken).ConfigureAwait(false))
            {
                int imdbId = sritem.ImdbId;
                DateTime? dt = sritem.UpdatedAt ?? sritem.UploadedAt;

                if (imdbId == 0 || dt == null)
                {
                    foreach (var fitem in files)
                    {
                        if (fitem.Name == "YavkA.net.txt")
                        {
                            fitem.Content.Seek(0, System.IO.SeekOrigin.Begin);
                            var reader = new System.IO.StreamReader(fitem.Content, Encoding.UTF8, true);
                            string info_text = reader.ReadToEnd();

                            if (dt == null)
                            {
                                var regexDate = new Regex(@"Качени на: (\d\d\d\d-\d\d-\d\d \d\d:\d\d:\d\d)");
                                var matchDate = regexDate.Match(info_text);
                                if (matchDate.Success && matchDate.Groups.Count > 0)
                                    dt = DateTime.Parse(matchDate.Groups[1].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                            }

                            if (imdbId == 0)
                            {
                                var regexImdbId = new Regex(@"iMDB ID: (tt(\d+))");
                                var matchImdb = regexImdbId.Match(info_text);
                                if (matchImdb.Success && matchImdb.Groups.Count > 2)
                                {
                                    var subImdb = matchImdb.Groups[1].ToString();
                                    imdbId = int.Parse(matchImdb.Groups[2].ToString());
                                }
                            }

                            break;
                        }
                    }
                }

                if (!si.MatchImdbId(imdbId, ref subScoreBase))
                {
                    //_logger.LogInformation($"Ignore result {subImdb} {subTitle} not matching IMDB ID");
                    //continue;
                }

                string subDate = dt != null ? dt?.ToString("g", CultureInfo.CurrentCulture) : "";
                subInfo += string.Format("<br>{0} | {1}", subDate, sritem.Uploader);

                var subFilesCount = files.SubCount;

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
                        _logger.LogInformation($"Ignore file: {file.Name} Score: {score}");
                        continue;
                    }

                    var item = new SubtitleInfo
                    {
                        ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                        Id = link.GetId(),
                        ProviderName = Name,
                        Name = file.Name,
                        PageLink = sritem.Link,
                        Format = file.GetExtSupportedByEmby(),
                        Author = sritem.Uploader,
                        Comment = subInfo + " | " + subFpsInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                        //CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                        DownloadCount = int.Parse(sritem.Downloads),
                        IsHashMatch = score >= GetOptions().HashMatchByScore,
                        IsForced = null,
                        IsSdh = null,
                        Score = score,
                        DateCreated = dt,
                    };

                    res.Add(item);
                }
            }

            return res;
        }

        protected async Task<(string, Dictionary<string, string>, SearchResultItem)> GetSubInfoPage(SearchResultItem sritem, CancellationToken cancellationToken)
        {
            var link = new Http.RequestCached
            {
                Url = sritem.Link,
                Referer = HttpReferer,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionSearch,
                CacheLifespan = GetOptions().Cache.GetSearchLife(),
            };

            using (var resp = await _downloader.GetResponse(link, cancellationToken))
            {
                var config = AngleSharp.Configuration.Default;
                var context = AngleSharp.BrowsingContext.New(config);
                var parser = new AngleSharp.Html.Parser.HtmlParser(context);
                var htmlDoc = parser.ParseDocument(resp.Content);

                // Parse updloaded and updated date time
                var calendars = htmlDoc.QuerySelectorAll("i[class='fa fa-calendar']");
                foreach (var cal in calendars)
                {
                    try
                    {
                        var calType = cal.NextSibling?.TextContent.Trim();
                        switch (calType)
                        {
                            case "Качени на:":
                            case "Обновени на:":
                                DateTime? dt = null;
                                var td = cal.ParentElement?.ParentElement; // get parent <td> element
                                var dateVal = td?.NextElementSibling?.QuerySelector("font")?.TextContent;
                                if (dateVal.IsNotNullOrWhiteSpace())
                                {
                                    dt = DateTime.ParseExact(dateVal, "HH:mm:ss dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
                                    if (calType == "Качени на:") sritem.UploadedAt = dt;
                                    if (calType == "Обновени на:") sritem.UpdatedAt = dt;
                                }
                                break;

                            default:
                                break;
                        }
                    }
                    catch { }
                }

                // Parse IMDB ID
                try
                {
                    var imdbTag = htmlDoc.QuerySelector("i[class='fab fa-imdb']");
                    var imdbLink = imdbTag?.ParentElement?.GetAttribute("href").Trim();
                    if (imdbLink.IsNotNullOrWhiteSpace())
                    {
                        var reg = new Regex(@"imdb.com/title/(tt(\d+))/?");
                        var match = reg.Match(imdbLink);
                        if (match != null && match.Groups.Count > 2)
                        {
                            var subImdb = match.Groups[1].Value;
                            sritem.ImdbId = int.Parse(match.Groups[2].ToString());
                        }
                    }
                }
                catch { }

                // Parse download POST parameters

                var formNodes = htmlDoc.GetElementsByTagName("form");
                foreach (var form in formNodes)
                {
                    var hidden = form.QuerySelectorAll("input[type='hidden']");
                    if (hidden == null) continue;

                    var postParams = new Dictionary<string, string>();
                    foreach (var hiddenItem in hidden)
                        postParams[hiddenItem.GetAttribute("name")] = hiddenItem.GetAttribute("value");

                    if (!postParams.ContainsKey("lng"))
                        continue;

                    _downloader.AddResponseToCache(link, resp);
                    return (form.GetAttribute("action"), postParams, sritem);
                }
                
                return (string.Empty, new Dictionary<string, string>(), sritem);
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
