using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using subbuzz.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers
{
    public class SubsUnacsNet : ISubtitleProvider, IHasOrder
    {
        private const string HttpReferer = "https://subsunacs.net/search.php";
        private readonly List<string> Languages = new List<string> { "bg", "en" };

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILibraryManager _libraryManager;

        public string Name => $"[{Plugin.NAME}] <b>subsunacs.net</b>";

        public IEnumerable<VideoContentType> SupportedMediaTypes =>
            new List<VideoContentType> { VideoContentType.Episode, VideoContentType.Movie };

        public int Order => 0;

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
            try
            {
                return await Download.ArchiveSubFile(_httpClient, id, HttpReferer).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.ErrorException(Name + ":GetSubtitles:Exception:", e);
            }

            return new SubtitleResponse();
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
            CancellationToken cancellationToken)
        {
            var res = new List<RemoteSubtitleInfo>();

            var languageInfo = _localizationManager.FindLanguageInfo(request.Language.AsSpan());
            var lang = languageInfo.TwoLetterISOLanguageName.ToLower();

            if (!Languages.Contains(lang))
            {
                return res;
            }

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

                _logger?.Info($"{Name} Request subtitle for '{searchText}', language={lang}, year={request.ProductionYear}");

                var opts = new HttpRequestOptions
                {
                    Url = "https://subsunacs.net/search.php",
                    UserAgent = Download.UserAgent,
                    Referer = HttpReferer,
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

                            var files = await Download.ArchiveSubFileNames(_httpClient, subLink, HttpReferer).ConfigureAwait(false);
                            foreach (var file in files)
                            {
                                string fileExt = file.Split('.').LastOrDefault().ToLower();
                                if (fileExt != "srt" && fileExt != "sub") continue;

                                var item = new RemoteSubtitleInfo
                                {
                                    ThreeLetterISOLanguageName = languageInfo.ThreeLetterISOLanguageName,
                                    Id = Utils.Base64UrlEncode(subLink + Download.UrlSeparator + file + Download.UrlSeparator + languageInfo.TwoLetterISOLanguageName),
                                    ProviderName = Name,
                                    Name = file,
                                    Format = fileExt,
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

    }
}
