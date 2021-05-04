using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.Providers
{
    interface ISubBuzzProvider
    {
        string Name { get; }
        IEnumerable<VideoContentType> SupportedMediaTypes { get; }

        Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken);
        Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken);
    }
}
