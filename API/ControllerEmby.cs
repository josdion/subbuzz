using MediaBrowser.Model.Services;
using subbuzz.Extensions;
using subbuzz.Providers.OpenSubtitlesAPI;
using subbuzz.Providers.OpenSubtitlesAPI.Models;
using subbuzz.Providers.TitloviAPI;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace subbuzz.API
{
    [Route("/subbuzz/ValidateOpenSubtitlesLoginInfo", "POST")]
    public class LoginInfoRequest : IReturn<object>
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string ApiKey { get; set; }
    }

    [Route("/subbuzz/ValidateTitloviLoginInfo", "POST")]
    public class TitloviLoginInfoRequest : IReturn<object>
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class ControllerEmby : IService

    {
        public async Task<object> Post(LoginInfoRequest body)
        {
            try
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

                    return new
                    {
                        Message = msg
                    };
                }

                return new
                {
                    Token = response.Data?.Token,
                    Downloads = response.Data?.User?.AllowedDownloads ?? 0
                };
            }
            catch
            {
                return new
                {
                    Message = "Unable to verify OpenSubtitles.com account"
                };
            }
        }

        public async Task<object> Post(TitloviLoginInfoRequest body)
        {
            try
            {
                var response = await Titlovi.LogInAsync(body.Username, body.Password, CancellationToken.None).ConfigureAwait(false);

                if (!response.Ok || string.IsNullOrWhiteSpace(response.Data?.Token))
                {
                    return new
                    {
                        Message = Titlovi.GetLoginErrorMessage(response)
                    };
                }

                return new
                {
                    Token = response.Data.Token,
                    UserId = response.Data.UserId,
                    UserName = response.Data.UserName,
                    ExpirationDate = response.Data.ExpirationDate
                };
            }
            catch
            {
                return new
                {
                    Message = "Unable to verify titlovi.com account"
                };
            }
        }
    }
}
