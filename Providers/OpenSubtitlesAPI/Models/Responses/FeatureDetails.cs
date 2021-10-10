using System.Text.Json.Serialization;

namespace subbuzz.Providers.OpenSubtitlesAPI.Models.Responses
{
    public class FeatureDetails
    {
        [JsonPropertyName("feature_type")]
        public string? FeatureType { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("imdb_id")]
        public int ImdbId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set; }

        [JsonPropertyName("season_number")]
        public int? SeasonNumber { get; set; }

        [JsonPropertyName("episode_number")]
        public int? EpisodeNumber { get; set; }
    }
}
