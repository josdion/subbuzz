using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using subbuzz.Extensions;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace subbuzz.Providers
{
    class SubsSabBz : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "subs.sab.bz";
        private const string ServerUrl = "http://subs.sab.bz";
        private const string HttpReferer = "http://subs.sab.bz/index.php?";
        private static readonly List<string> Languages = new List<string> { "bg", "en" };
        private static readonly string[] CacheRegionSub = { "subs.sab.bz", "sub" };
        private static readonly string[] CacheRegionSearch = { "subs.sab.bz", "search" };

        private readonly Logger _logger;
        private readonly Http.Download _downloader;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        private static Dictionary<string, string> InconsistentTvs = new Dictionary<string, string>
        {
            { "Marvel's Daredevil", "Daredevil" },
            { "Marvel's Luke Cage", "Luke Cage" },
            { "Marvel's Iron Fist", "Iron Fist" },
            { "Marvel's Jessica Jones", "Jessica Jones" },
            { "DC's Legends of Tomorrow", "Legends of Tomorrow" },
            { "Doctor Who (2005)", "Doctor Who" },
            { "Star Trek: Deep Space Nine", "Star Trek DS9" },
            { "Star Trek: The Next Generation", "Star Trek TNG" },
        };

        private static Dictionary<string, string> InconsistentMovies = new Dictionary<string, string>
        {
            { "Back to the Future Part", "Back to the Future" },
        };

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;


        public SubsSabBz(
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
                if (!GetOptions().EnableSubssabbz)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(
                    request,
                    _localizationManager,
                    _libraryManager,
                    "{0} {1:D2}x{2:D2}",
                    "{0} Season {1}",
                    InconsistentTvs,
                    InconsistentMovies);

                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                if (!Languages.Contains(si.Lang))
                {
                    return res;
                }

                var tasks = new List<Task<List<SubtitleInfo>>>();

                if (si.ImdbId.IsNotNullOrWhiteSpace())
                {
                   // search by IMDB Id
                   string urlImdb = string.Format(
                        "{0}/index.php?act=search&movie={1}&select-language={2}&upldr=&yr=&&release=&imdb={3}&sort=dd&",
                        ServerUrl,
                        request.ContentType == VideoContentType.Episode ?
                            string.Format("{0:D2}x{1:D2}", si.SeasonNumber ?? 0, si.EpisodeNumber ?? 0) : "",
                        si.Lang == "en" ? "1" : "2",
                        si.ImdbId
                        );

                    tasks.Add(SearchUrl(urlImdb, null, si, cancellationToken));

                    if (request.ContentType == VideoContentType.Episode && 
                        si.ImdbIdEpisode.IsNotNullOrWhiteSpace() &&
                        si.ImdbId != si.ImdbIdEpisode)
                    {
                        string urlImdbEp = string.Format(
                            "{0}/index.php?act=search&movie=&select-language={1}&upldr=&yr=&&release=&imdb={2}&sort=dd&",
                            ServerUrl,
                            si.Lang == "en" ? "1" : "2",
                            si.ImdbIdEpisode
                            );

                        tasks.Add(SearchUrl(urlImdbEp, null, si, cancellationToken));
                    }
                }

                if (si.SearchText.IsNotNullOrWhiteSpace())
                {
                    // search for movies/series by title
                    var post_params = new Dictionary<string, string>
                    {
                        { "act", "search"},
                        { "movie", si.SearchText },
                        { "select-language", si.Lang == "en" ? "1" : "2" },
                        { "upldr", "" },
                        { "yr", request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "" },
                        { "release", "" }
                    };

                    tasks.Add(SearchUrl($"{ServerUrl}/index.php?", post_params, si, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && si.SearchSeason.IsNotNullOrWhiteSpace() && (si.SeasonNumber ?? 0) > 0)
                {
                    // search for episodes in season packs
                    var post_params = new Dictionary<string, string>
                    {
                        { "act", "search"},
                        { "movie", si.SearchSeason },
                        { "select-language", si.Lang == "en" ? "1" : "2" },
                        { "upldr", "" },
                        { "yr", "" },
                        { "release", "" }
                    };

                    tasks.Add(SearchUrl($"{ServerUrl}/index.php?", post_params, si, cancellationToken));
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

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, Dictionary<string, string> post_params, SearchInfo si, CancellationToken cancellationToken)
        {
            try
            {
                var link = new Http.RequestCached
                {
                    Url = url,
                    Referer = HttpReferer,
                    Type = post_params == null ? Http.Request.RequestType.GET : Http.Request.RequestType.POST,
                    PostParams = post_params,
                    CacheKey = url + ((post_params == null) ? string.Empty : $"post={{{string.Join(",", post_params)}}}"),
                    CacheRegion = CacheRegionSearch,
                    CacheLifespan = GetOptions().Cache.GetSearchLife(),
                };

                using (var resp = await _downloader.GetResponse(link, cancellationToken))
                {
                    var res = await ParseHtml(resp.Content, si, cancellationToken);
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

        protected async Task<List<SubtitleInfo>> ParseHtml(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var config = AngleSharp.Configuration.Default;
            var context = AngleSharp.BrowsingContext.New(config);
            var parser = new AngleSharp.Html.Parser.HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var trNodes = htmlDoc.QuerySelectorAll("tr.subs-row");
            if (trNodes == null || trNodes.Length <= 0) return res;

            for (int i = 0; i < trNodes.Length; i++)
            {
                var tdNodes = trNodes[i].QuerySelectorAll("td");
                if (tdNodes == null || tdNodes.Length < 12) continue;

                var linkNode = tdNodes[3].QuerySelector("a");
                if (linkNode == null) continue;

                string subLink = linkNode.GetAttribute("href");
                var regexLink = new Regex(@"(?<g1>.*index.php\?)(?<g2>s=.*&amp;)?(?<g3>.*)");
                subLink = regexLink.Replace(subLink, "${g1}${g3}");

                string subTitle = linkNode.TextContent;
                string subYear = linkNode.NextSibling.TextContent.Trim(new[] { ' ', '(', ')' });
                subTitle += $" ({subYear})";

                var subScoreBase = new SubtitleScore();
                si.MatchTitle(subTitle, ref subScoreBase);

                string subNotes = linkNode.GetAttribute("onmouseover");
                var regex = new Regex(@"ddrivetip\(\'<div.*/></div>(.*)\',\'#[0-9]+\'\)");
                subNotes = regex.Replace(subNotes, "$1");
                int subInfoIdx = subNotes.LastIndexOf("<b>Доп. инфо</b>");
                string subInfoBase = subInfoIdx >= 0 ? subNotes.Substring(subInfoIdx + 17) : "";

                string subInfo = Utils.TrimString(subInfoBase, "<br />");
                subInfo = subInfo.Replace("<br /><br />", "<br />").Replace("<br /><br />", "<br />");
                subInfo = subTitle + (subInfo.IsNullOrWhiteSpace() ? "" : "<br>" + subInfo);

                string subDate = tdNodes[4].TextContent;
                DateTime? dt = null;
                try
                {
                    var regexDate = new Regex(@"<b>Добавени</b>: ([0-9a-zA-z,: ]*)<br/>");
                    var m = regexDate.Match(subNotes);
                    if (m.Success && m.Groups.Count > 1)
                    {
                        dt = DateTime.Parse(m.Groups[1].Value);
                        subDate = dt?.ToString("g", CultureInfo.CurrentCulture);
                    }
                }
                catch (Exception)
                {
                }

                string subNumCd = tdNodes[6].TextContent;
                string subFps = tdNodes[7].TextContent;
                string subUploader = tdNodes[8].TextContent;

                int imdbId = 0;
                string subImdb = "";
                var linkImdb = tdNodes[9].QuerySelector("a");
                if (linkImdb != null)
                {
                    var reg = new Regex(@"imdb.com/title/(tt(\d+))/?");
                    var match = reg.Match(linkImdb.GetAttribute("href"));
                    if (match != null && match.Groups.Count > 2)
                    {
                        subImdb = match.Groups[1].Value;
                        imdbId = int.Parse(match.Groups[2].ToString());
                    }
                }

                if (!si.MatchImdbId(imdbId, ref subScoreBase))
                {
                    //_logger.LogInformation($"Ignore result {subImdb} {subTitle} not matching IMDB ID");
                    //continue;
                }

                si.MatchFps(subFps, ref subScoreBase);

                string subDownloads = tdNodes[10].TextContent;

                string subRating = "0";
                var rtImgNode = tdNodes[11].QuerySelector("img");
                if (rtImgNode != null)
                {
                    subRating = rtImgNode.GetAttribute("title");
                    subRating = subRating.Replace("Rating: ", "").Trim();
                }

                subInfo += string.Format("<br>{0} | {1}", subDate, subUploader);

                var link = new Http.RequestSub
                {
                    Url = subLink,
                    Referer = HttpReferer,
                    Type = Http.Request.RequestType.GET,
                    CacheKey = subLink,
                    CacheRegion = CacheRegionSub,
                    CacheLifespan = GetOptions().Cache.GetSubLife(),
                    Lang = si.LanguageInfo.TwoLetterISOLanguageName,
                    Fps = Download.LinkSub.FpsFromStr(subFps),
                    FpsVideo = si.VideoFps,
                };

                using (var files = await _downloader.GetArchiveFiles(link, cancellationToken).ConfigureAwait(false))
                {
                    int subFilesCount = files.SubCount;
                    foreach (var file in files)
                    {
                        if (!file.IsSubfile()) continue;

                        link.File = file.Name;
                        link.Fps = file.Sub.FpsRequested;

                        string subFpsInfo = subFps;
                        if (file.Sub.FpsRequested != null && file.Sub.FpsDetected != null &&
                            Math.Abs(file.Sub.FpsRequested ?? 0 - file.Sub.FpsDetected ?? 0) > 0.001)
                        {
                            subFpsInfo = $"{file.Sub.FpsRequested?.ToString(CultureInfo.InvariantCulture)} ({file.Sub.FpsDetected?.ToString(CultureInfo.InvariantCulture)})";
                            link.Fps = file.Sub.FpsDetected;
                        }

                        SubtitleScore subScore = (SubtitleScore)subScoreBase.Clone();
                        si.MatchFps(link.Fps, ref subScore);

                        bool scoreVideoFileName = subFilesCount == 1 && subInfoBase.ContainsIgnoreCase(si.FileName);
                        bool ignorMutliDiscSubs = subFilesCount > 1;

                        float score = si.CaclScore(file.Name, subScore, scoreVideoFileName, ignorMutliDiscSubs);
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
                            Name = $"<a href='{subLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file.Name}</a>",
                            Format = file.GetExtSupportedByEmby(),
                            Author = subUploader,
                            Comment = subInfo + " | " + subFpsInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            DateCreated = dt,
                            CommunityRating = Convert.ToInt32(subRating),
                            DownloadCount = Convert.ToInt32(subDownloads),
                            IsHashMatch = score >= GetOptions().HashMatchByScore,
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
