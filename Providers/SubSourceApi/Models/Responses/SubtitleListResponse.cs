using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class SubtitleListResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public List<SubtitleResult> Data { get; set; } = new List<SubtitleResult>();

        [JsonPropertyName("pagination")]
        public Pagination? Pagination { get; set; }
    }
}
