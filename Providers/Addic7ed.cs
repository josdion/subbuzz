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
using System.Web;

namespace subbuzz.Providers
{
    public class Addic7ed : ISubBuzzProvider
    {
        internal const string NAME = "Addic7ed.com";
        private const string ServerUrl = "https://www.addic7ed.com";
        private static readonly string[] CacheRegionSub = { "addic7ed.com", "sub" };
        private static readonly string[] CacheRegionSearch = { "addic7ed.com", "search" };
        private static readonly string[] CacheRegionData = { "addic7ed.com", "data" };

        private readonly Logger _logger;
        private readonly Http.Download _downloader;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        private PluginConfiguration GetOptions()
            => Plugin.Instance.Configuration;

        public string Name => $"[{Plugin.NAME}] <b>{NAME}</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        private static readonly Dictionary<string, string> LangMap = new Dictionary<string, string>
        {
            { "Chinese (Simplified)",   "chs" },
            { "Chinese (Traditional)",  "cht" },
            { "French (Canadian)",      "frc" },
            { "Portuguese (Brazilian)", "pob" },
            { "Serbian (Cyrillic)",     "srp" },
            { "Serbian (Latin)",        "srp" },
            { "Spanish",                "spa" },
            { "Spanish (Argentina)",    "spa" },
            { "Spanish (Latin America)","spa" },
            { "Spanish (Spain)",        "spa" },
        };

        public Addic7ed(
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
                if (!GetOptions().EnableAddic7ed)
                {
                    // provider is disabled
                    return res;
                }

                SearchInfo si = SearchInfo.GetSearchInfo(
                    request,
                    _localizationManager,
                    _libraryManager,
                    "{0} {1:D2}x{2:D2}",
                    "{0} Season {1}");

                _logger.LogInformation($"Request subtitle for '{si.SearchText}', language={si.Lang}, year={request.ProductionYear}, IMDB={si.ImdbId}");

                if (request.ContentType.Equals(VideoContentType.Episode) && si.SeasonNumber != null && si.EpisodeNumber != 0)
                    res = await SearchEpisode(si, cancellationToken).ConfigureAwait(false);
                else
                if (request.ContentType.Equals(VideoContentType.Movie))
                    res = await SearchMovie(si, cancellationToken).ConfigureAwait(false);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Search error: {e}");
            }

            watch.Stop();
            _logger.LogInformation($"Search duration: {watch.ElapsedMilliseconds / 1000.0} sec. Subtitles found: {res.Count}");

            return res;
        }

        protected async Task<List<SubtitleInfo>> SearchEpisode(SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();
            
            var (showId, showName) = await GetShowId(si, cancellationToken).ConfigureAwait(false);
            if (showId.IsNullOrWhiteSpace()) { return res; }
            
            var episodes = await GetEpisodes(showId, showName, si, cancellationToken).ConfigureAwait(false);
            foreach (var episode in episodes)
            {
                if (episode.Season != si.SeasonNumber) continue;
                if (episode.Episode != si.EpisodeNumber) continue;
                if (episode.Language == null || !si.IsRequestedLanguage(episode.Language)) continue;

                res.Add(GetSubtitleInfo(episode, si));
            }

            return res;
        }

        protected async Task<List<SubtitleInfo>> SearchMovie(SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SubtitleInfo>();
            string searchText = si.SearchText;
            if (si.Year != null) searchText += $" ({si.Year})";

            var pages = await Search(searchText, "movie/", cancellationToken);
            var movies = await GetDetails(pages, true, cancellationToken).ConfigureAwait(false);

            foreach (var movie in movies)
            {
                if (movie.Language == null || !si.IsRequestedLanguage(movie.Language)) continue;
                res.Add(GetSubtitleInfo(movie, si));
            }

            return res;
        }

        protected SubtitleInfo GetSubtitleInfo(SearchResultItem resItem, SearchInfo si)
        {
            SubtitleScore subScore = new SubtitleScore();
            float score = si.CaclScore(resItem.FileTitle, subScore, false);

            var link = new Http.RequestSub
            {
                Url = resItem.Download,
                Referer = ServerUrl,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionSub,
                Lang = si.LanguageInfo.TwoLetterISOLanguageName,
            };

            string subInfo = $"{resItem.FullTitle}<br>Version: {resItem.Version}";
            if (resItem.Comment.IsNotNullOrWhiteSpace()) subInfo += $"<br>{resItem.Comment}";
            if (resItem.UploadInfo.IsNotNullOrWhiteSpace()) subInfo += $"<br>{resItem.UploadInfo}";
            if (resItem.DownloadInfo.IsNotNullOrWhiteSpace()) subInfo += $"<br>{resItem.DownloadInfo}";
            if (resItem.EditedInfo.IsNotNullOrWhiteSpace()) subInfo += $" - last {resItem.EditedInfo}";
            if (resItem.LastEdited != null)
                subInfo += "<br>" + resItem.LastEdited?.ToString("d", CultureInfo.CurrentCulture) + " | " + resItem.Uploader;
            else
                subInfo += $"<br>{resItem.Uploader}";

            var item = new SubtitleInfo
            {
                ThreeLetterISOLanguageName = si.LanguageInfo.ThreeLetterISOLanguageName,
                Id = link.GetId(),
                ProviderName = Name,
                Name = $"<a href='{resItem.PageLink}' target='_blank' is='emby-linkbutton' class='button-link' style='margin:0;'>{resItem.FileTitle}</a>",
                Format = "srt",
                Author = resItem.Uploader,
                Comment = subInfo + " | Score: " + score.ToString("0.00", CultureInfo.InvariantCulture) + " %",
                DateCreated = resItem.LastEdited,
                CommunityRating = null,
                DownloadCount = resItem.DownloadCount,
                IsHashMatch = score >= Plugin.Instance.Configuration.HashMatchByScore,
                IsForced = null,
                Sdh = resItem.HearingImpaired,
                Score = score,
            };

            return item;
        }

        private async Task<Dictionary<string, string>> GetShows(CancellationToken cancellationToken)
        {
            Dictionary<string, string> shows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var link = new Http.RequestCached
            {
                Url = ServerUrl + "/",
                Referer = ServerUrl,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionData,
                CacheLifespan = 7 * 24 * 60, // one week
            };

            using var resp = await _downloader.GetResponse(link, cancellationToken).ConfigureAwait(false);

            var config = AngleSharp.Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(resp.Content);
            var qsShow = htmlDoc?.QuerySelector("#qsShow");
            if (qsShow == null) return shows;

            foreach (var option in qsShow.QuerySelectorAll("option"))
            {
                shows.Add(option.TextContent.Trim(new char[] { ' ', '\t', '\n' }), option.GetAttribute("value"));
            }

            if (shows.Count > 6000)
                _downloader.AddResponseToCache(link, resp);

            return shows;
        }

        private async Task<(string, string)> GetShowId(SearchInfo si, CancellationToken cancellationToken)
        {
            var shows = await GetShows(cancellationToken).ConfigureAwait(false);

            if ((si.Year ?? 0) > 0)
            {
                string key = $"{si.TitleSeries} ({si.Year})";
                if (shows.ContainsKey(key)) return (shows[key], key);
            }

            if (shows.ContainsKey(si.TitleSeries)) return (shows[si.TitleSeries], si.TitleSeries);

            return (null, null);
        }

        private async Task<List<SearchResultItem>> GetEpisodes(string showId, string showName, SearchInfo si, CancellationToken cancellationToken)
        {
            var res = new List<SearchResultItem>();

            var link = new Http.RequestCached
            {
                Url = ServerUrl + $"/ajax_loadShow.php?show={showId}&season={si.SeasonNumber}",
                Referer = ServerUrl,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionSearch,
                CacheLifespan = GetOptions().Cache.GetSearchLife(),
            };

            using var resp = await _downloader.GetResponse(link, cancellationToken).ConfigureAwait(false);

            var config = AngleSharp.Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(resp.Content);
            var tblRows = htmlDoc?.QuerySelectorAll("tr.completed");
            if (tblRows == null) return res;

            foreach (var tr in tblRows)
            {
                var td = tr.QuerySelectorAll("td");
                if (td == null || td.Length < 11) continue;

                bool seasonParsed = int.TryParse(td[0].TextContent, out int s);
                bool episodeParsed = int.TryParse(td[1].TextContent, out int e);

                if (!seasonParsed || !episodeParsed || s != si.SeasonNumber || e != si.EpisodeNumber)
                    continue;

                string lang = td[3].TextContent.Trim(new char[] { ' ', '\t', '\n' });
                if (LangMap.ContainsKey(lang)) lang = LangMap[lang];

                SearchResultItem resItem = new SearchResultItem
                {
                    Season = seasonParsed ? s : null,
                    Episode = episodeParsed ? e : null,
                    Title = td[2].TextContent,
                    PageLink = GetFullUrl(td[2].QuerySelector("a")?.GetAttribute("href")),
                    Language = lang,
                    Version = td[4].TextContent,
                    Completed = td[5].TextContent.ContainsIgnoreCase("Completed"),
                    HearingImpaired = td[6].TextContent.IsNotNullOrWhiteSpace(),
                    Corrected = td[7].TextContent.IsNotNullOrWhiteSpace(),
                    HD = td[8].TextContent.IsNotNullOrWhiteSpace(),
                    Download = GetFullUrl(td[9].QuerySelector("a")?.GetAttribute("href")),
                    Id = td[10].QuerySelector("input")?.GetAttribute("value").Split('/'),
                };
                
                resItem.FullTitle = $"{showName} - {resItem.Season:00}x{resItem.Episode:00} - {resItem.Title}";
                resItem.FileTitle = $"{resItem.FullTitle}.{resItem.Version}";
                res.Add(resItem);
            }

            _downloader.AddResponseToCache(link, resp);
            return await GetDetails(res, false, cancellationToken).ConfigureAwait(false);
        }

        private async Task<List<SearchResultItem>> GetDetails(List<SearchResultItem> res, bool addMissing, CancellationToken cancellationToken)
        {
            if (res.Count <= 0) return res;

            var pageLinks = new HashSet<string>();
            foreach (var r in res) pageLinks.Add(r.PageLink);

            foreach (var pageLink in pageLinks)
            {
                var link = new Http.RequestCached
                {
                    Url = pageLink,
                    Referer = ServerUrl,
                    Type = Http.RequestType.GET,
                    CacheRegion = CacheRegionSearch,
                    CacheLifespan = GetOptions().Cache.GetSearchLife(),
                };

                using var resp = await _downloader.GetResponse(link, cancellationToken).ConfigureAwait(false);

                var config = AngleSharp.Configuration.Default;
                var context = BrowsingContext.New(config);
                var parser = new HtmlParser(context);
                var htmlDoc = parser.ParseDocument(resp.Content);

                var pageTitle = htmlDoc.QuerySelector("span.titulo")?.FirstChild.TextContent.Trim(new char[] { ' ', '\t', '\n' });

                var tables = htmlDoc.QuerySelectorAll("td > table");
                if (tables == null) 
                    continue;

                foreach (var table in tables)
                {
                    try
                    {
                        var rows = table.QuerySelectorAll("tr");
                        if (rows == null || rows.Length < 4) continue;

                        // get version 

                        var version = rows[0].QuerySelector("td.NewsTitle")?.TextContent;
                        var versionMatches = Regex.Match(version, @"Version (.+?), Duration:");
                        if (versionMatches != null && versionMatches.Groups.Count > 1)
                            version = versionMatches.Groups[1].Value;

                        // get comment
                        var comment = rows[1].QuerySelector("td.newsDate")?.TextContent.Trim(new char[] { ' ', '\t', '\n' });

                        // get uploader info

                        DateTime? dtUploaded = null;
                        var tagUploader = rows[0].QuerySelectorAll("td")[1].QuerySelectorAll("a").LastOrDefault();
                        var uploaderName = tagUploader.TextContent;
                        var uploadInfo = $"{tagUploader.PreviousSibling.TextContent} {uploaderName} {tagUploader.NextSibling.TextContent}".Trim(new char[] { ' ', '\t', '\n' });
                        var uploadMatches = Regex.Match(uploadInfo, @"(\d+) days ago");
                        if (uploadMatches != null && uploadMatches.Groups.Count > 1 && int.TryParse(uploadMatches.Groups[1].Value, out int uploadedBefore))
                            dtUploaded = DateTime.Now - new TimeSpan(uploadedBefore, 0, 0, 0, 0);
                        
                        // get subtitle for the current version

                        for (var rowIndex = 2; (rowIndex + 1) < rows.Length; rowIndex += 2)
                        {
                            bool addItem = false;
                            var row1Downloads = rows[rowIndex].QuerySelectorAll("a.buttonDownload");
                            if (row1Downloads.Length < 1) continue;

                            var downloadLink = row1Downloads[row1Downloads.Length-1]?.GetAttribute("href").TrimStart('/');
                            SearchResultItem resItem = res.Find(x => x.IsIdEqual(downloadLink));
                            if (resItem == null && addMissing)
                            {
                                var row1Language = rows[rowIndex].QuerySelector("td.language");
                                var lang = row1Language?.FirstChild?.TextContent.Trim(new char[] { ' ', '\t', '\n' });
                                if (LangMap.ContainsKey(lang)) lang = LangMap[lang];

                                // Add new item
                                resItem = new SearchResultItem 
                                {
                                    Title = pageTitle,
                                    PageLink = pageLink,
                                    Language = lang,
                                    Version = version,
                                    Download = GetFullUrl(downloadLink),
                                    Id = downloadLink.Substring(downloadLink.IndexOf('/')+1).Split('/'),
                                    FullTitle = pageTitle,
                                };
                                resItem.FileTitle = $"{resItem.FullTitle}.{resItem.Version}";
                                addItem = true;
                            }

                            resItem.Uploader = uploaderName;
                            resItem.UploadInfo = uploadInfo;
                            resItem.LastEdited = dtUploaded;
                            resItem.Comment = comment;

                            var row2td = rows[rowIndex + 1].QuerySelectorAll("td");

                            if (row2td[0].QuerySelector("img[title='Corrected']") != null)
                                resItem.Corrected = true;

                            if (row2td[0].QuerySelector("img[title='Hearing Impaired']") != null)
                                resItem.HearingImpaired = true;

                            resItem.DownloadInfo = row2td[0]?.TextContent.Trim(new char[] { ' ', '\t', '\n' });
                            resItem.EditedInfo = row2td[1]?.QuerySelector("a")?.NextSibling?.TextContent?.Trim(new char[] { ' ', '\t', '\n' });

                            var downloadMatches = Regex.Match(resItem.DownloadInfo, @"(\d+) times edited.+?(\d+) Downloads.+?(\d+) sequences");
                            if (downloadMatches != null && downloadMatches.Groups.Count > 3 && int.TryParse(downloadMatches.Groups[2].Value, out int downloads))
                                resItem.DownloadCount = downloads;

                            var editedMatches = Regex.Match(resItem.EditedInfo, @"edited (\d+) days ago");
                            if (editedMatches != null && editedMatches.Groups.Count > 1 && int.TryParse(editedMatches.Groups[1].Value, out int editedBefore))
                                resItem.LastEdited = DateTime.Now - new TimeSpan(editedBefore, 0, 0, 0, 0);

                            if (addItem)
                                res.Add(resItem);
                        }
                    }
                    catch { }
                }

                _downloader.AddResponseToCache(link, resp);
            }

            return res;
        }

        protected async Task<List<SearchResultItem>> Search(string searchText, string searchType, CancellationToken cancellationToken)
        {
            var res = new List<SearchResultItem>();

            var link = new Http.RequestCached
            {
                Url = ServerUrl + $"/srch.php?search={HttpUtility.UrlEncode(searchText)}&Submit=Search",
                Referer = ServerUrl,
                Type = Http.RequestType.GET,
                CacheRegion = CacheRegionSearch,
                CacheLifespan = GetOptions().Cache.GetSearchLife(),
            };

            using var resp = await _downloader.GetResponse(link, cancellationToken).ConfigureAwait(false);

            var config = AngleSharp.Configuration.Default;
            var context = BrowsingContext.New(config);
            var parser = new HtmlParser(context);
            var htmlDoc = parser.ParseDocument(resp.Content);

            var tblRows = htmlDoc.QuerySelector("table.tabel")?.QuerySelectorAll("tr");
            foreach (var row in tblRows)
            {
                var td = row.QuerySelectorAll("td");
                if (td.Length < 2) continue;

                var linkTag = td[1].QuerySelector("a");
                var pageLink = linkTag.GetAttribute("href");

                if (!pageLink.StartsWith(searchType)) 
                    continue;

                SearchResultItem item = new SearchResultItem
                {
                    Title = linkTag.TextContent,
                    PageLink = GetFullUrl(pageLink),
                };

                res.Add(item);
            }

            _downloader.AddResponseToCache(link, resp);
            return res;
        }

        private string GetFullUrl(string url)
        {
            if (url.IsNotNullOrWhiteSpace())
            {
                if (url.StartsWith("https:") || url.StartsWith("http:")) return url;
                return $"{ServerUrl}/{url.TrimStart('/')}";
            }
            return url;
        }

        protected class SearchResultItem
        {
            public int? Season { get; set; }
            public int? Episode { get; set; }
            public string Title { get; set; }
            public string Language { get; set; }
            public string Version { get; set; }
            public bool Completed { get; set; }
            public bool HearingImpaired { get; set; }
            public bool Corrected { get; set; }
            public bool HD { get; set; }
            public string Download { get; set; }
            public string[] Id { get; set; } // language/episode id/version
            public string PageLink { get; set; }
            public string FullTitle { get; set; }
            public string FileTitle { get; set; }
            public string Uploader { get; set; }
            public int? DownloadCount { get; set; }
            public string UploadInfo { get; set; }
            public string EditedInfo { get; set; }
            public string DownloadInfo { get; set; }
            public string Comment { get; set; }

            public DateTime? LastEdited { get; set; }

            public bool IsIdEqual(string id)
            {
                if (id.IsNullOrWhiteSpace()) return false;
                return IsIdEqual(id.Split('/'));
            }

            public bool IsIdEqual(string[] id)
            {
                if (Id == null || id == null) return false;
                if (Id.Length < 2 || id.Length < 2) return false;
                return Id.Last() == id.Last() && Id[Id.Length - 2] == id[id.Length - 2];
            }
        }

    }
}
