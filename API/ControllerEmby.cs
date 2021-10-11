using MediaBrowser.Model.Services;
using System;
using System.Collections.Generic;
using System.Text;
using subbuzz.Providers.OpenSubtitlesAPI;
using subbuzz.Providers.OpenSubtitlesAPI.Models;
using MediaBrowser.Controller.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace subbuzz.API
{
    [Route("/subbuzz/ValidateOpenSubtitlesLoginInfo", "POST")]
    public class LoginInfoRequest : IReturn<object>
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string ApiKey { get; set; }
    }

    public class ControllerEmby : IService

    {
       public async Task<object> Post(LoginInfoRequest body)
        {
            var response = await OpenSubtitles.LogInAsync(body.Username, body.Password, body.ApiKey, CancellationToken.None).ConfigureAwait(false);

            if (!response.Ok)
            {
                var msg = $"{response.Code}{(response.Body.Length < 150 ? $" - {response.Body}" : string.Empty)}";

                if (response.Body.Contains("message\":", StringComparison.Ordinal))
                {
                    var err = JsonSerializer.Deserialize<ErrorResponse>(response.Body);
                    if (err != null)
                    {
                        msg = string.Equals(err.Message, "You cannot consume this service", StringComparison.Ordinal)
                            ? "Invalid API key provided" : err.Message;
                    }
                }

                return new { Message = msg };
            }

            return new
            {
                Token = response.Data?.Token,
                Downloads = response.Data?.User?.AllowedDownloads ?? 0
            };
        }
    }
}
