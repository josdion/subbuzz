using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class Posters
    {
        [JsonPropertyName("small")]
        public string Small { get; set; } = string.Empty;

        [JsonPropertyName("medium")]
        public string Medium { get; set; } = string.Empty;

        [JsonPropertyName("large")]
        public string Large { get; set; } = string.Empty;

        [JsonPropertyName("original")]
        public string Original { get; set; } = string.Empty;
    }
}
