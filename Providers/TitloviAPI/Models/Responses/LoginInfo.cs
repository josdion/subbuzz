using System.Text.Json.Serialization;

namespace subbuzz.Providers.TitloviAPI.Models.Responses
{
    public class LoginInfo
    {
        [JsonPropertyName("Token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("UserId")]
        public int UserId { get; set; }

        [JsonPropertyName("UserName")]
        public string UserName { get; set; } = string.Empty;

        // Local time of the titlovi.com server without an offset, e.g. 2026-11-01T09:12:20.123
        [JsonPropertyName("ExpirationDate")]
        public string ExpirationDate { get; set; } = string.Empty;
    }
}
