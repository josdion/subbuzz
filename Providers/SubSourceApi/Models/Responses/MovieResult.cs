using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class MovieResult
    {
        [JsonPropertyName("movieId")]
        public int MovieId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("alternateTitle")]
        public string AlternateTitle { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("releaseYear")]
        public int? ReleaseYear { get; set; }

        [JsonPropertyName("imdbId")]
        public string ImdbId { get; set; } = string.Empty;

        [JsonPropertyName("tmdbId")]
        public string TmdbId { get; set; } = string.Empty;

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("subtitleCount")]
        public int SubtitleCount { get; set; }

        [JsonPropertyName("posters")]
        public Posters? Posters { get; set; }
    }
}
