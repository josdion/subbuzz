using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace subbuzz.Providers.SubSourceApi.Models.Responses
{
    public class MovieSearchResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public List<MovieResult> Data { get; set; } = new List<MovieResult>();
    }
}
