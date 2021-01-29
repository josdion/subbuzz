using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;
using MediaBrowser.Model.Globalization;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using SharpCompress.Readers;
using System.Linq;
using System.Globalization;
using subbuzz.Helpers;

namespace subbuzz.Providers
{
    public class SubsUnacsNet : ISubtitleProvider, IHasOrder
    {
        private const string UrlSeparator = "*|*";

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => "subsunacs.net";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 2;

        public SubsUnacsNet(ILogger logger, IFileSystem fileSystem, IHttpClient httpClient,
            ILocalizationManager localizationManager, ILibraryManager libraryManager)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _httpClient = httpClient;
            _localizationManager = localizationManager;
            _libraryManager = libraryManager;
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            try
            {
                BaseItem libItem = _libraryManager.FindByPath(request.MediaPath, false);
                if (libItem == null)
                {
                    _logger.Info($"{Name} No library info for {request.MediaPath}");
                    return res;
                }

                string searchText = "";

                if (request.ContentType == VideoContentType.Movie)
                {
                    searchText = libItem.OriginalTitle;
                }
                else
                if (request.ContentType == VideoContentType.Episode)
                {
                    Episode ep = libItem as Episode;
                    searchText = String.Format("{0} {1:D2} {2:D2}", ep.Series.OriginalTitle, request.ParentIndexNumber ?? 0, request.IndexNumber ?? 0);
                }
                else
                {
                    return res;
                }

                var language = _localizationManager.FindLanguageInfo(request.Language.AsSpan());
                var lang = language.TwoLetterISOLanguageName.ToLower();

                _logger?.Info($"{Name} Request subtitle for '{searchText}', language={lang}, year={request.ProductionYear}");

                if (lang != "bg" && lang != "en")
                {
                    return res;
                }

                var opts = new HttpRequestOptions
                {
                    Url = "https://subsunacs.net/search.php",
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0",
                    Referer = "https://subsunacs.net/index.php",
                    TimeoutMs = 10000, //10 seconds timeout
                    EnableKeepAlive = false,
                };

                var post_params = new Dictionary<string, string>
                {
                    { "m", searchText },
                    { "l", lang != "en" ? "0" :"1" },
                    { "c", "" }, // country
                    { "y", request.ContentType == VideoContentType.Movie ? Convert.ToString(request.ProductionYear) : "" },
                    { "action", "   Търси   " },
                    { "a", "" }, // actor
                    { "d", "" }, // director
                    { "u", "" }, // uploader
                    { "g", "" }, // genre
                    { "t", "" },
                    { "imdbcheck", "1" }                
                };

                opts.SetPostData(post_params);

                using (var response = await _httpClient.Post(opts).ConfigureAwait(false))
                {
                    using (var reader = new StreamReader(response.Content, System.Text.Encoding.GetEncoding(1251)))
                    {
                        var html = await reader.ReadToEndAsync().ConfigureAwait(false);

                        var htmlDoc = new HtmlDocument();
                        htmlDoc.LoadHtml(html);

                        var trNodes = htmlDoc.DocumentNode.SelectNodes("//tr[@onmouseover]");
                        if (trNodes == null) return res;

                        for (int i = 0; i < trNodes.Count; i++)
                        {
                            var tdNodes = trNodes[i].SelectNodes(".//td");
                            if (tdNodes == null || tdNodes.Count < 6) continue;

                            HtmlNode linkNode = tdNodes[0].SelectSingleNode("a[@href]");
                            if (linkNode == null) continue;

                            string subLink = "https://subsunacs.net" + linkNode.Attributes["href"].Value;
                            
                            string subNotes = linkNode.Attributes["title"].DeEntitizeValue;

                            var regex = new Regex(@"(?:.*<b>Инфо: </b><br>)(.*)(?:</div>)");
                            string subInfo = regex.Replace(subNotes, "$1");

                            string subNumCd = tdNodes[1].InnerText;
                            string subFps = tdNodes[2].InnerText;
                            string subRating = tdNodes[3].SelectSingleNode(".//img").Attributes["title"].Value;
                            string subUploader = tdNodes[5].InnerText;
                            string subDownloads = tdNodes[6].InnerText;

                            var files = await GetSubFileNames(subLink);
                            foreach (var file in files)
                            {
                                var item = new RemoteSubtitleInfo
                                {
                                    ThreeLetterISOLanguageName = language.ThreeLetterISOLanguageName,
                                    Id = Utils.Base64UrlEncode(subLink + UrlSeparator + file + UrlSeparator + language.TwoLetterISOLanguageName),
                                    ProviderName = $"[{Plugin.NAME}] <b>{Name}</b>",
                                    Name = file,
                                    Format = file.Split('.').LastOrDefault().ToUpper(),
                                    Author = subUploader,
                                    Comment = subInfo,
                                    //DateCreated = DateTimeOffset.Parse(subDate),
                                    CommunityRating = float.Parse(subRating, CultureInfo.InvariantCulture),
                                    DownloadCount = int.Parse(subDownloads),
                                    IsHashMatch = false,
                                    IsForced = false,
                                };

                                res.Add(item);
                            }
                        }

                        return res;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException(Name + ":Search:Exception:", e);
            }

            return res;
        }

        private async Task<IEnumerable<string>> GetSubFileNames(string link)
        {
            var res = new List<string>();

            var opts = new HttpRequestOptions
            {
                Url = link,
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:85.0) Gecko/20100101 Firefox/85.0",
                Referer = "https://subsunacs.net/search.php",
                TimeoutMs = 10000, //10 seconds timeout
                EnableKeepAlive = false,
            };

            try
            {
                using (var response = await _httpClient.Get(opts).ConfigureAwait(false))
                {
                    var arcreader = ReaderFactory.Open(response);
                    while (arcreader.MoveToNextEntry())
                    {
                        string fileExt = arcreader.Entry.Key.Split('.').LastOrDefault().ToLower();

                        if (!arcreader.Entry.IsDirectory && (fileExt == "srt" || fileExt == "sub"))
                        {
                            res.Add(arcreader.Entry.Key);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException(Name + ":GetSubFileNames:Exception:", e);
            }

            return res;
        }
    }
}
