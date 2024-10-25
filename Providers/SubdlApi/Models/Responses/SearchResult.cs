using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubdlApi.Models.Responses
{
    public class SearchResult
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("results")]
        public IReadOnlyList<ResultItem> Results { get; set; } = Array.Empty<ResultItem>();

        [JsonPropertyName("subtitles")]
        public IReadOnlyList<Subtitle> Subtitles { get; set; } = Array.Empty<Subtitle>();
    }
}
