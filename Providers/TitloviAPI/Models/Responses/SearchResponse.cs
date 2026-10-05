using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.TitloviAPI.Models.Responses
{
    public class SearchResponse
    {
        [JsonPropertyName("ResultsFound")]
        public int ResultsFound { get; set; }

        [JsonPropertyName("PagesAvailable")]
        public int PagesAvailable { get; set; }

        [JsonPropertyName("CurrentPage")]
        public int CurrentPage { get; set; }

        [JsonPropertyName("SubtitleResults")]
        public List<SubtitleResult> SubtitleResults { get; set; } = new List<SubtitleResult>();
    }
}
