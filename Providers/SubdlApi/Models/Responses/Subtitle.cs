using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubdlApi.Models.Responses
{
    public class Subtitle
    {
        [JsonPropertyName("release_name")]
        public string Release { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("lang")]
        public string Lang { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("subtitlePage")]
        public string SubPage { get; set; }

        [JsonPropertyName("season")]
        public int? Season { get; set; }

        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("hi")]
        public bool? HearingImpaired { get; set; }

        [JsonPropertyName("comment")]
        public string Comment { get; set; }

        [JsonPropertyName("releases")]
        public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();

        [JsonPropertyName("episode_from")]
        public int? EpisodeFrom {  get; set; }

        [JsonPropertyName("episode_end")]
        public int? EpisodeEnd { get; set; }

        [JsonPropertyName("full_season")]
        public bool? FullSeason { get; set; }
    }
}
