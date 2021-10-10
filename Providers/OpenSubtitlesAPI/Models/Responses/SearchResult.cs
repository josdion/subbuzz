using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class SearchResult
    {
        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("data")]
        public IReadOnlyList<ResponseData> Data { get; set; } = Array.Empty<ResponseData>();
    }
}
