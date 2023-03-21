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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

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
    class SubsUnacsNet : ISubBuzzProvider, IHasOrder
    {
        internal const string NAME = "subsunacs.net";
        private const string ServerUrl = "https://subsunacs.net";
        private const string HttpReferer = "https://subsunacs.net/search.php";
        private static readonly List<string> Languages = new List<string> { "bg", "en" };
        private static readonly string[] CacheRegionSub = { "subsunacs.net", "sub" };

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;

        private static Dictionary<string, string> InconsistentTvs = new Dictionary<string, string>
        {
            { "Marvel's Daredevil", "Daredevil" },
            { "Marvel's Luke Cage", "Luke Cage" },
            { "Marvel's Iron Fist", "Iron Fist" },
            { "DC's Legends of Tomorrow", "Legends of Tomorrow" },
            { "Doctor Who (2005)", "Doctor Who" },
            { "Star Trek: Deep Space Nine", "Star Trek DS9" },
            { "Star Trek: The Next Generation", "Star Trek TNG" },
            { "La Casa de Papel", "Money Heist" },
            { "Star Wars: Andor", "Andor" },
        };

        private static Dictionary<string, string> InconsistentMovies = new Dictionary<string, string>
        {
            { "Back to the Future Part III", "Back to the Future 3" },
            { "Back to the Future Part II", "Back to the Future 2" },
            { "Bill & Ted Face the Music", "Bill Ted Face the Music" },
            { "The Protégé", "The Protege"},
        };

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

        public SubsUnacsNet(
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
            downloader = new Download(http, logger, NAME);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                return await downloader.GetSubtitles(id, HttpReferer, cancellationToken).ConfigureAwait(false);
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
                if (!Plugin.Instance.Configuration.EnableSubsunacsNet)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(
                    request,
                    _localizationManager,
                    _libraryManager,
                    "{0} {1:D2}x{2:D2}",
                    "{0} {1:D2} Season",
                    InconsistentTvs,
                    InconsistentMovies);

                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                if (!Languages.Contains(si.Lang) || String.IsNullOrEmpty(si.SearchText))
                {
                    return res;
                }

                si.SearchText = si.SearchText.Replace(':', ' ').Replace("  ", " ");
                si.SearchEpByName = si.SearchEpByName.Replace(':', ' ').Replace("  ", " ");

                var tasks = new List<Task<List<SubtitleInfo>>>();

                if (!String.IsNullOrEmpty(si.SearchText))
                {
                    // search for movies/series by title
                    var post_params = GetPostParams(
                        si.SearchText,
                        si.Lang != "en" ? "0" : "1",
                        request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "");

                    tasks.Add(SearchUrl($"{ServerUrl}/search.php", post_params, si, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && !String.IsNullOrEmpty(si.SearchEpByName) && (si.SeasonNumber ?? 0) == 0)
                {
                    // Search for special episodes by name
                    var post_params = GetPostParams(si.SearchEpByName, si.Lang != "en" ? "0" : "1", "");
                    tasks.Add(SearchUrl($"{ServerUrl}/search.php", post_params, si, cancellationToken));
                }

                if (request.ContentType == VideoContentType.Episode && !String.IsNullOrWhiteSpace(si.SearchSeason) && (si.SeasonNumber ?? 0) > 0)
                {
                    // search for episodes in season packs
                    var post_params = GetPostParams(si.SearchSeason, si.Lang != "en" ? "0" : "1", "");
                    tasks.Add(SearchUrl($"{ServerUrl}/search.php", post_params, si, cancellationToken));
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
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"{NAME}: Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected Dictionary<string, string> GetPostParams(string m, string l, string y)
        {
            return new Dictionary<string, string>
                {
                    { "m", m }, // search text
                    { "l", l }, // language - 0: bulgarian, 1: english
                    { "c", "" }, // country
                    { "y", y }, // year
                    { "action", "   Търси   " },
                    { "a", "" }, // actor
                    { "d", "" }, // director
                    { "u", "" }, // uploader
                    { "g", "" }, // genre
                    { "t", "" },
                    { "imdbcheck", "1" }
                };
        }

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, Dictionary<string, string> post_params, SearchInfo si, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"{NAME}: " + (post_params != null ? $"POST: {url} -> " + post_params["m"] : $"GET: {url}"));

                using (var html = await downloader.GetStream(url, HttpReferer, post_params, cancellationToken))
                {
                    return await ParseHtml(html, si, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: Search error: {e}");
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

            var trNodes = htmlDoc.QuerySelectorAll("tr[onmouseover]");
            foreach (var tr in trNodes)
            {
                var tdNodes = tr.GetElementsByTagName("td");
                if (tdNodes == null || tdNodes.Count() < 6) continue;

                var linkTag = tdNodes[0].QuerySelector("a");
                if (linkTag == null) continue;

                string subLink = ServerUrl + linkTag.GetAttribute("href");
                string subTitle = linkTag.InnerHtml;

                var year = linkTag.NextElementSibling;
                string subYear = year != null ? year.InnerHtml.Replace("&nbsp;", " ") : "";
                subYear = subYear.Trim(new[] { ' ', '(', ')' });
                subTitle += $" ({subYear})";

                var subScoreBase = new SubtitleScore();
                si.MatchTitle(subTitle, ref subScoreBase);

                string subNotes = linkTag.GetAttribute("title");
                string subDate = string.Empty;
                string subInfoBase = string.Empty; ;
                string subInfo = string.Empty; ;

                var regex = new Regex(@"(?:.*<b>Дата: </b>)(?<date>.*)(?:<br><b>Инфо: </b><br>)(?<notes>.*)");
                var regexImg = new Regex(@"<img[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                var subNotesMatch = regex.Match(subNotes);

                if (subNotesMatch.Success)
                {
                    subDate = subNotesMatch.Groups["date"].Value;
                    subInfoBase = subNotesMatch.Groups["notes"].Value.Replace("</div>", string.Empty);
                    subInfoBase = regexImg.Replace(subInfoBase, string.Empty);

                    subInfo = Utils.TrimString(subInfoBase, "<br>");
                    subInfo = subInfo.Replace("<br><br>", "<br>").Replace("<br><br>", "<br>");
                    subInfo = subInfo.Replace("&nbsp;", " ");
                    subInfo = subTitle + (String.IsNullOrWhiteSpace(subInfo) ? "" : "<br>" + subInfo);
                }

                string subNumCd = tdNodes[1].InnerHtml;
                string subFps = tdNodes[2].InnerHtml;

                string subRating = "0";
                var rtImgNode = tdNodes[3].QuerySelector("img");
                if (rtImgNode != null) subRating = rtImgNode.GetAttribute("title");

                var linkUploader = tdNodes[5].QuerySelector("a");
                string subUploader = linkUploader == null ? "" : linkUploader.InnerHtml;
                string subDownloads = tdNodes[6].InnerHtml;

                DateTime? dt = null;
                try
                {
                    dt = DateTime.Parse(subDate, CultureInfo.CreateSpecificCulture("bg-BG"));
                    subDate = dt?.ToString("g", CultureInfo.CurrentCulture);
                }
                catch (Exception)
                {
                }

                subInfo += string.Format("<br>{0} | {1}", subDate, subUploader);

                Download.LinkSub link = new Download.LinkSub
                {
                    Url = subLink,
                    CacheKey = subLink,
                    CacheRegion = CacheRegionSub,
                    Lang = si.LanguageInfo.TwoLetterISOLanguageName,
                    Fps = Download.LinkSub.FpsFromStr(subFps),
                    FpsVideo = si.VideoFps,
                };

                using (var files = await downloader.GetArchiveFiles(link, HttpReferer, cancellationToken).ConfigureAwait(false))
                {
                    int imdbId = 0;
                    string subImdb = "";
                    foreach (var fitem in files)
                    {
                        if (Regex.IsMatch(fitem.Name, @"subsunacs\.net_\d*\.txt"))
                        {
                            fitem.Content.Seek(0, System.IO.SeekOrigin.Begin);
                            var reader = new System.IO.StreamReader(fitem.Content, Encoding.UTF8, true);
                            string info_text = reader.ReadToEnd();

                            var regexImdbId = new Regex(@"imdb.com/title/(tt(\d+))/?");
                            var match = regexImdbId.Match(info_text);
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
                        //_logger.LogInformation($"{NAME}: Ignore result {subImdb} {subTitle} not matching IMDB ID");
                        //continue;
                    }

                    var subFilesCount = files.CountSubFiles();

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
                        if (score == 0 || score < Plugin.Instance.Configuration.MinScore)
                        {
                            _logger.LogInformation($"{NAME}: Ignore file: {file} Score: {score}");
                            continue;
                        }

                        var item = new SubtitleInfo
                        {
                            ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                            Id = link.GetId(),
                            ProviderName = Name,
                            Name = $"<a href='{subLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{file}</a>",
                            Format = file.GetExtSupportedByEmby(),
                            Author = subUploader,
                            Comment = subInfo + " | " + subFpsInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                            DateCreated = dt,
                            CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                            DownloadCount = int.Parse(subDownloads),
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
