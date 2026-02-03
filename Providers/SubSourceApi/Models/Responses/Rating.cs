using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class Rating
    {
        [JsonPropertyName("good")]
        public int Good { get; set; }

        [JsonPropertyName("bad")]
        public int Bad { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }
}
