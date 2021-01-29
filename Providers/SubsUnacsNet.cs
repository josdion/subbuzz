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

        public int Order => 1;

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

            var item = new RemoteSubtitleInfo
            {
                ThreeLetterISOLanguageName = "bul",
                Id = "test",
                ProviderName = $"[{Plugin.NAME}] <b>{Name}</b>",
                Name = "TEst Test",
                Format = "srt",
                Author = "subUploader",
                Comment = "subInfo",
                //DateCreated = DateTimeOffset.Parse(subDate),
                //CommunityRating = Convert.ToInt32(subRating),
                DownloadCount = Convert.ToInt32(1),//subDownloads),
                IsHashMatch = false,
                IsForced = false,
            };

            res.Add(item);

            return res;
        }

    }
}