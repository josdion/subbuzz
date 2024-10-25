using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubdlApi.Models.Responses
{
    public class ResultItem
    {
        [JsonPropertyName("sd_id")]
        public int? SdId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("imdb_id")]
        public string ImdbId { get; set; }

        [JsonPropertyName("tmdb_id")]
        public int? TmdbId { get; set;}

        [JsonPropertyName("first_air_date")]
        public DateTime? AirData { get; set; }

        [JsonPropertyName("release_date")]
        public DateTime? ReleaseData { get; set; }

        [JsonPropertyName("year")]
        public int? Year {  get; set; }
    }
}
