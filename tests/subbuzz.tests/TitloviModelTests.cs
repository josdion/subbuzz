using subbuzz.Providers.TitloviAPI;
using subbuzz.Providers.TitloviAPI.Models;
using subbuzz.Providers.TitloviAPI.Models.Responses;
using System.IO;
using System.Net;
using System.Text.Json;
using Xunit;

namespace subbuzz.tests
{
    public class TitloviModelTests
    {
        [Fact]
        public void SearchResponse_DeserializesApiResponse()
        {
            var result = JsonSerializer.Deserialize<SearchResponse>(Fixture.Read("titlovi_search.json"));

            Assert.NotNull(result);
            Assert.Equal(2, result.ResultsFound);
            Assert.Equal(1, result.PagesAvailable);
            Assert.Equal(1, result.CurrentPage);
            Assert.Equal(2, result.SubtitleResults.Count);

            var movie = result.SubtitleResults[0];
            Assert.Equal(415854, movie.Id);
            Assert.Equal("Avatar", movie.Title);
            Assert.Equal(2009, movie.Year);
            Assert.Equal(Titlovi.TypeMovie, movie.Type);
            Assert.Equal("https://titlovi.com/download/?type=1&mediaid=415854", movie.Link);
            Assert.Equal(-1, movie.Season);
            Assert.Equal(-1, movie.Episode);
            Assert.Equal("Slovenski", movie.Lang);
            Assert.Equal("2025-09-14T18:22:41.73", movie.Date);
            Assert.Equal(128, movie.DownloadCount);
            Assert.Equal(5.0f, movie.Rating);
            Assert.Equal("Extended Collectors Edition UHD BluRay 2160p HDR10+ DV HEVC DTS-HD MA 5.1 x265-E", movie.Release);

            var episode = result.SubtitleResults[1];
            Assert.Equal(318842, episode.Id);
            Assert.Equal(Titlovi.TypeEpisode, episode.Type);
            Assert.Equal(5, episode.Season);
            Assert.Equal(3, episode.Episode);
            Assert.Equal("Cirilica", episode.Lang);
            Assert.Equal(0f, episode.Rating);
        }

        [Fact]
        public void SearchResponse_ToleratesMissingFields()
        {
            var result = JsonSerializer.Deserialize<SearchResponse>("{\"SubtitleResults\":[{\"Id\":1,\"Title\":\"Avatar\"}]}");

            Assert.NotNull(result);
            var sub = Assert.Single(result.SubtitleResults);
            Assert.Equal(1, sub.Id);
            Assert.Null(sub.Year);
            Assert.Null(sub.Season);
            Assert.Null(sub.Episode);
            Assert.Null(sub.DownloadCount);
            Assert.Null(sub.Rating);
            Assert.Equal(string.Empty, sub.Lang);
            Assert.Equal(string.Empty, sub.Release);
            Assert.Equal(string.Empty, sub.Date);
        }

        [Fact]
        public void LoginInfo_DeserializesApiResponse()
        {
            var info = JsonSerializer.Deserialize<LoginInfo>(Fixture.Read("titlovi_login.json"));

            Assert.NotNull(info);
            Assert.Equal("9b85e39d-76df-478a-a43c-f7a9af6d4c7f", info.Token);
            Assert.Equal(123456, info.UserId);
            Assert.Equal("subbuzz", info.UserName);
            Assert.Equal("2026-11-01T09:12:20.1234567", info.ExpirationDate);
            Assert.False(Titlovi.IsTokenExpired(info.ExpirationDate, new System.DateTime(2026, 10, 2)));
        }

        [Fact]
        public void ApiResponse_ParsesBodyOnlyForSuccessfulRequests()
        {
            var ok = new ApiResponse<SearchResponse>(new HttpResponse { Code = HttpStatusCode.OK, Body = Fixture.Read("titlovi_search.json") });
            Assert.True(ok.Ok);
            Assert.NotNull(ok.Data);
            Assert.Equal(2, ok.Data.SubtitleResults.Count);

            var unauthorized = new ApiResponse<SearchResponse>(new HttpResponse { Code = HttpStatusCode.Unauthorized, Body = string.Empty });
            Assert.False(unauthorized.Ok);
            Assert.Null(unauthorized.Data);
        }

        [Fact]
        public void ApiResponse_ThrowsOnInvalidJson()
        {
            var response = new HttpResponse { Code = HttpStatusCode.OK, Body = "<html>Cloudflare</html>" };

            Assert.Throws<JsonException>(() => new ApiResponse<SearchResponse>(response, "context"));
        }

        [Fact]
        public void ApiResponse_SurvivesCacheRoundTrip()
        {
            var original = new ApiResponse<SearchResponse>(new HttpResponse { Code = HttpStatusCode.OK, Body = Fixture.Read("titlovi_search.json") });

            using var stream = new MemoryStream();
            JsonSerializer.Serialize(stream, original);
            stream.Seek(0, SeekOrigin.Begin);
            var cached = JsonSerializer.Deserialize<ApiResponse<SearchResponse>>(stream);

            Assert.NotNull(cached);
            Assert.Equal(original.Code, cached.Code);
            Assert.True(cached.Ok);
            Assert.Equal(2, cached.Data.SubtitleResults.Count);
            Assert.Equal("The Wire", cached.Data.SubtitleResults[1].Title);
        }

        [Fact]
        public void GetLoginErrorMessage_ExplainsUnauthorizedAndOtherFailures()
        {
            var unauthorized = new ApiResponse<LoginInfo>(new HttpResponse { Code = HttpStatusCode.Unauthorized });
            Assert.Contains("API access", Titlovi.GetLoginErrorMessage(unauthorized));

            var serverError = new ApiResponse<LoginInfo>(new HttpResponse { Code = HttpStatusCode.BadGateway, Body = "Bad Gateway" });
            Assert.Equal("BadGateway - Bad Gateway", Titlovi.GetLoginErrorMessage(serverError));

            var noToken = new ApiResponse<LoginInfo>(new HttpResponse { Code = HttpStatusCode.OK, Body = "{}" });
            Assert.Contains("token", Titlovi.GetLoginErrorMessage(noToken));
        }
    }
}
