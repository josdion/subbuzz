using AngleSharp;
using AngleSharp.Html.Parser;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MediaBrowser.Controller.Subtitles;
using subbuzz.Helpers;
using subbuzz.Extensions;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

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
    public class Subf2m : ISubBuzzProvider
    {
        internal const string NAME = "Subf2m";
        private const string ServerUrl = "https://subf2m.co";

        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;
        private Download downloader;

        private readonly Dictionary<string, string> _languages = new Dictionary<string, string>
        {
            { "alb", "albanian" }, { "ara", "arabic" }, { "arm", "armenian" }, { "aze", "azerbaijani" },
            { "baq", "basque" }, { "bel", "belarusian" }, { "ben", "bengali" }, { "bos", "bosnian" }, { "bul", "bulgarian" }, { "bur", "burmese" },
            { "cat", "catalan" }, { "chs", "chinese-bg-code" }, { "cht", "chinese-bg-code" }, { "cze", "czech" },
            { "dan", "danish" }, { "dut", "dutch" },
            { "eng", "english" }, { "epo", "esperanto" }, { "est", "estonian" },
            { "fin", "finnish" }, { "fre", "french" }, { "geo", "georgian" }, { "ger", "german" }, { "gre", "greek" },
            { "heb", "hebrew" }, { "hin", "hindi" }, { "hrv", "croatian" }, { "hun", "hungarian" },
            { "ice", "icelandic" }, { "ind", "indonesian" }, { "ita", "italian" },
            { "jav", "japanese" },
            { "kan", "kannada" }, { "kin", "kinyarwanda" }, { "kor", "korean" }, { "kur", "kurdish" },
            { "lav", "latvian" }, { "lit", "lithuanian" },
            { "mac", "macedonian" }, { "mal", "malayalam" }, { "may", "malay" }, { "mon", "mongolian" },
            { "nep", "nepali" }, { "nno", "norwegian" }, { "nob", "norwegian" }, { "nor", "norwegian" },
            { "per", "farsi_persian" }, { "pob", "brazillian-portuguese" }, { "pol", "polish" }, { "por", "portuguese" },
            { "rum", "romanian" }, { "rus", "russian" },
            { "sin", "sinhala" }, { "slo", "slovak" }, { "slv", "slovenian" }, { "som", "somali" }, { "spa", "spanish" },
            { "srp", "serbian" }, { "sun", "sundanese" }, { "swa", "swahili" }, { "swe", "swedish" },
            { "tam", "tamil" }, { "tel", "telugu" }, { "tgl", "tagalog" }, { "tha", "thai" }, { "tur", "turkish" },
            { "ukr", "ukrainian" }, { "urd", "urdu" },
            { "vie", "vietnamese" },
            { "yor", "yoruba" },
        };

        private static readonly string[] _seasonNumbers = {
            "",
            "First",        "Second",       "Third",        "Fourth",       "Fifth",
            "Sixth",        "Seventh",      "Eighth",       "Ninth",        "Tenth",
            "Eleventh",     "Twelfth",      "Thirteenth",   "Fourteenth",   "Fifteenth",
            "Sixteenth",    "Seventeenth",  "Eightheenth",  "Nineteenth",   "Tweentieth",
        };

        private static readonly Regex ImdbUrlRegex = new Regex(@"imdb.com/title/tt(?<imdbid>\d{7,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SubDetailsRegex = new Regex(@"(?<par>\w[\w|\s]+)\:(?:\s*)(?<val>[^\n\t]*\S)(?:\s*)(?<val2>[^\n\t]*\S)?", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public Subf2m(
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
            downloader = new Download(http);

        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            try
            {
                return await downloader.GetArchiveSubFile(
                    id,
                    ServerUrl,
                    Encoding.GetEncoding(1251),
                    true,
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
            var res = new List<SubtitleInfo>();

            try
            {
                if (!Plugin.Instance.Configuration.EnableSubf2m)
                {
                    // provider is disabled
                    return res;
                }

                string seasonFormat = string.Empty;
                if (request.ParentIndexNumber != null &&
                    request.ParentIndexNumber < _seasonNumbers.Length)
                {
                    seasonFormat = $"{"{0}"} - {_seasonNumbers[request.ParentIndexNumber ?? 0]} Season";
                }

                SearchInfo si = SearchInfo.GetSearchInfo(request, _localizationManager, _libraryManager, seasonFormat);
                _logger.LogInformation($"{NAME}: Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}");

                string langPage;
                if (!_languages.TryGetValue(si.LanguageInfo.ThreeLetterISOLanguageName, out langPage))
                {
                    _logger.LogInformation($"{NAME}: Language not supported: {si.LanguageInfo.ThreeLetterISOLanguageName}");
                    return res;
                }

                if (si.SearchText.IsNullOrWhiteSpace() || si.ImdbIdInt <= 0)
                {
                    _logger.LogInformation($"{NAME}: Search info or IMDB ID missing");
                    return res;
                }

                var url = $"{ServerUrl}/subtitles/searchbytitle?query={HttpUtility.UrlEncode(si.SearchText)}&l=";
                return await SearchUrl(url, si, langPage, cancellationToken).ConfigureAwait(false);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: Search error: {e}");
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> SearchUrl(string url, SearchInfo si, string langPage, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"{NAME}: GET: {url}");

                using (var html = await downloader.GetStream(url, ServerUrl, null, cancellationToken).ConfigureAwait(false))
                {
                    return await ParseSearchResult(html, si, langPage, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: Search error: {e}");
                return new List<SubtitleInfo>();
            }
        }

        protected async Task<List<SubtitleInfo>> ParseSearchResult(System.IO.Stream html, SearchInfo si, string langPage, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var resDiv = htmlDoc.QuerySelector("div.search-result");
            if (resDiv == null) return res;

            var resUl = resDiv.QuerySelector("h2.exact")?.NextElementSibling;

            if (resUl == null)
                resUl = resDiv.QuerySelector("h2.close")?.NextElementSibling;

            if (resUl == null)
            {
                if (si.VideoType == VideoContentType.Movie)
                {
                    resUl = resDiv.QuerySelector("h2.popular")?.NextElementSibling;
                }
            }

            if (resUl == null)
                return res;

            var listItems = resUl.QuerySelectorAll("li");
            foreach (var li in listItems)
            {
                var link = li.QuerySelector("a");
                if (link == null) continue;

                res = await GetSubtitlesListPage(ServerUrl + link.GetAttribute("href") + $"/{langPage}", si, cancellationToken);
                if (res.Count > 0) return res;
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> GetSubtitlesListPage(string url, SearchInfo si, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"{NAME}: GET: {url}");

                using (var html = await downloader.GetStream(url, ServerUrl, null, cancellationToken))
                {
                    return await ParseSubtitlesList(html, si, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: Search error: {e}");
                return new List<SubtitleInfo>();
            }
        }

        protected class SubData
        {
            public List<string> Releases = new List<string>();
            public string Uploader = string.Empty;
            public string Comment = string.Empty;
        };

        protected async Task<List<SubtitleInfo>> ParseSubtitlesList(System.IO.Stream html, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();
            var links = new Dictionary<string, SubData>();

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var imdbTag = htmlDoc.QuerySelector("a.imdb");
            if (imdbTag == null) return res;

            var imdbLink = imdbTag.GetAttribute("href");
            var imdbMatch = ImdbUrlRegex.Match(imdbLink);
            if (imdbMatch == null || !imdbMatch.Success)
                return res;

            _ = int.TryParse(imdbMatch.Groups["imdbid"].Value, out int imdbId);
            if (imdbId <= 0 || (si.ImdbIdInt != imdbId && si.ImdbIdEpisodeInt != imdbId))
                return res;

            var tbl = htmlDoc.QuerySelector("ul.sublist");
            var trs = tbl?.QuerySelectorAll("li.item");
            foreach (var tr in trs)
            {
                try
                {
                    var tagInfo = tr.QuerySelector("div.col-info");
                    if (tagInfo == null) continue;

                    var tagUl = tagInfo.QuerySelector("ul.scrolllist");
                    var tagLi = tagUl?.QuerySelectorAll("li");
                    if (tagLi == null || tagLi.Length < 1) continue;
                    
                    var releases = new List<string>();
                    foreach (var item in tagLi)
                    {
                        releases.Add(item.InnerHtml.Trim());
                    }

                    var tagLink = tr.QuerySelector("a.download");
                    if (tagLink == null || !tagLink.HasAttribute("href")) continue;
                    var subLink = ServerUrl + tagLink.Attributes["href"].Value;

                    if (!links.ContainsKey(subLink))
                    {
                        links[subLink] = new SubData();

                        var tagComment = tr.QuerySelector("div.comment-col");

                        var uploader = tagComment.QuerySelector("a")?.TextContent.Trim(new char[] { ' ', '\t', '\n' });
                        links[subLink].Uploader = uploader.IsNotNullOrWhiteSpace() ? uploader : "Anonymous";

                        var comment = tagComment.QuerySelector("p")?.TextContent;
                        if (comment != null) links[subLink].Comment = comment;
                    }

                    links[subLink].Releases.AddRange(releases);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"{NAME}: Parsing subtitles list error: {e}");
                }
            }

            foreach (var link in links)
            {
                try
                {
                    if (si.VideoType == VideoContentType.Episode)
                    {
                        bool skip = true;
                        foreach (var rel in link.Value.Releases)
                        {
                            Parser.EpisodeInfo epInfo = Parser.Episode.ParseTitle(rel);
                            if (epInfo == null) continue;

                            if (epInfo.SeasonNumber != si.SeasonNumber) continue;
                            if (epInfo.EpisodeNumbers == null || epInfo.EpisodeNumbers.Length <= 0)
                            {
                                // season pack
                                skip = false;
                                break;
                            }

                            if (Array.Find(epInfo.EpisodeNumbers, x => x == si.EpisodeNumber) == si.EpisodeNumber)
                            {
                                skip = false;
                                break;
                            }
                        }

                        if (skip)
                        {
                            _logger.LogInformation($"{NAME}: Skipping: {link.Key} - {link.Value.Releases[0]}");
                            continue;
                        }
                    }

                    res.AddRange(await GetSubtitlePage(link.Key, link.Value, si, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"{NAME}: Parsing subtitles {link.Key} error: {e}");
                }
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> GetSubtitlePage(string url, SubData subData, SearchInfo si, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"{NAME}: GET: {url}");

                using (var html = await downloader.GetStream(url, ServerUrl, null, cancellationToken).ConfigureAwait(false))
                {
                    return await ParseSubtitlePage(html, url, subData, si, cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{NAME}: GET: {url}: subtitle page error: {e}");
                return new List<SubtitleInfo>();
            }
        }

        protected async Task<List<SubtitleInfo>> ParseSubtitlePage(System.IO.Stream html, string urlPage, SubData subData, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();

            var subScoreBase = new SubtitleScore();
            subScoreBase.AddMatch("imdb");

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(html);

            var tagSubs = htmlDoc.QuerySelector("div.subtitle");
            var tagHeader = tagSubs?.QuerySelector("div.header");
            var tagDetails = tagSubs?.QuerySelectorAll("div.details ul > li");

            var downloadLink = tagHeader?.QuerySelector("div.download")?.QuerySelector("a")?.GetAttribute("href");
            if (downloadLink.IsNullOrWhiteSpace()) return res;
            downloadLink = ServerUrl + downloadLink;

            string title = tagHeader?.QuerySelector("span[itemprop='name']")?.TextContent.Trim();
            string subInfo = title.IsNotNullOrWhiteSpace() ? title : string.Empty;
            subInfo += "<br>" + string.Join("<br>", subData.Releases.ToArray());

            int numFiles = 0;
            int subDownloads = 0;
            float fps = 0;
            DateTime? dt = null;
            string subDate = string.Empty;
            if (tagDetails != null)
                foreach (var tag in tagDetails)
                {
                    var match = SubDetailsRegex.Match(tag.TextContent);
                    if (!match.Success) continue;
                    switch (match.Groups["par"].Value)
                    {
                        case "Online":
                            try
                            {
                                dt = DateTime.Parse(match.Groups["val"].Value, CultureInfo.InvariantCulture);
                                subDate = dt?.ToString("g", CultureInfo.CurrentCulture);
                            }
                            catch (Exception)
                            {
                            }
                            break;

                        case "Hearing Impaired":
                            break;

                        case "Framerate":
                            _ = float.TryParse(match.Groups["val"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out fps);
                            break;

                        case "Files":
                            var parts = match.Groups["val"].Value.Split('(');
                            if (parts.Length > 0)
                                _ = int.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out numFiles);
                            break;

                        case "Rated":
                            break;

                        case "Downloads":
                            _ = int.TryParse(match.Groups["val"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out subDownloads);
                            break;
                    }
                }

            subInfo += subData.Comment.IsNotNullOrWhiteSpace() ? $"<br>{subData.Comment}" : "";
            subInfo += $"<br>{subDate} | {subData.Uploader}";

            string subFps = (si.VideoFps ?? 25).ToString(CultureInfo.InvariantCulture);
            if (fps > 0)
            {
                subFps = fps.ToString(CultureInfo.InvariantCulture);
                subInfo += $" | {subFps}";
                si.MatchFps(subFps, ref subScoreBase);
            }

            if (subData.Releases.Count == 1)
                si.MatchTitle(subData.Releases[0], ref subScoreBase);

            var files = await downloader.GetArchiveFileNames(downloadLink, ServerUrl, cancellationToken).ConfigureAwait(false);

            foreach (var (fileName, fileExt) in files)
            {
                bool scoreVideoFileName = files.Count == 1 && subData.Releases.Find(x => x.EqualsIgnoreCase(si.FileName)).IsNotNullOrWhiteSpace();
                bool ignorMutliDiscSubs = files.Count > 1;

                float score = si.CaclScore(fileName, subScoreBase, scoreVideoFileName, ignorMutliDiscSubs);
                if (score == 0 || score < Plugin.Instance.Configuration.MinScore)
                {
                    _logger.LogInformation($"{NAME}: Ignore file: {fileName} Page: {urlPage} Socre: {score}");
                    continue;
                }

                var subItmem = new SubtitleInfo
                {
                    ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                    Id = Download.GetId(downloadLink, fileName, si.LanguageInfo.TwoLetterISOLanguageName, subFps),
                    ProviderName = Name,
                    Name = $"<a href='{urlPage}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{fileName}</a>",
                    Format = "srt",
                    Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                    DateCreated = dt,
                    DownloadCount = subDownloads,
                    IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                    IsForced = false,
                    Score = score,
                };

                res.Add(subItmem);
            }

            return res;
        }
    }
}
