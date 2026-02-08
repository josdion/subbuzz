using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class Contributor
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("displayname")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
