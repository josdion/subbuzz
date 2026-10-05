using subbuzz.Providers.TitloviAPI;
using System;
using Xunit;

namespace subbuzz.tests
{
    public class TitloviUrlAndDateTests
    {
        [Fact]
        public void GetDownloadUrl_PointsToTheSiteDownloadEndpoint()
        {
            Assert.Equal("https://titlovi.com/download/?type=1&mediaid=415854", Titlovi.GetDownloadUrl(415854, Titlovi.TypeMovie));
            Assert.Equal("https://titlovi.com/download/?type=2&mediaid=318842", Titlovi.GetDownloadUrl(318842, Titlovi.TypeEpisode));
        }

        [Theory]
        [InlineData("Avatar", 415854, "https://titlovi.com/titlovi/avatar-415854/")]
        [InlineData("Avatar: The Way of Water", 1, "https://titlovi.com/titlovi/avatar-the-way-of-water-1/")]
        [InlineData("Đavolja varoš - Čudo", 7, "https://titlovi.com/titlovi/davolja-varos-cudo-7/")]
        [InlineData("  Fast & Furious  ", 402924, "https://titlovi.com/titlovi/fast-furious-402924/")]
        [InlineData("", 5, "https://titlovi.com/titlovi/titl-5/")]
        [InlineData(null, 5, "https://titlovi.com/titlovi/titl-5/")]
        public void GetPageUrl_BuildsReadableSlugFromTitle(string? title, int id, string expected)
        {
            Assert.Equal(expected, Titlovi.GetPageUrl(title, id));
        }

        [Fact]
        public void ParseDate_ReadsServerLocalTimeWithAndWithoutFraction()
        {
            Assert.Equal(new DateTime(2025, 4, 27, 3, 31, 48, 123), Titlovi.ParseDate("2025-04-27T03:31:48.123"));
            Assert.Equal(new DateTime(2019, 3, 2, 21, 5, 0), Titlovi.ParseDate("2019-03-02T21:05:00"));
            Assert.Equal(new DateTime(2026, 11, 1, 9, 12, 20).AddTicks(1234567), Titlovi.ParseDate("2026-11-01T09:12:20.1234567"));
        }

        [Fact]
        public void ParseDate_ReadsLegacyJsonDate()
        {
            DateTime expected = DateTimeOffset.FromUnixTimeMilliseconds(1745724708000).LocalDateTime;

            Assert.Equal(expected, Titlovi.ParseDate("/Date(1745724708000)/"));
            Assert.Equal(expected, Titlovi.ParseDate("\\/Date(1745724708000+0200)\\/"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("yesterday")]
        public void ParseDate_ReturnsNullForUnknownValues(string? value)
        {
            Assert.Null(Titlovi.ParseDate(value));
        }

        [Fact]
        public void IsTokenExpired_TreatsTokenAsExpiredOneDayInAdvance()
        {
            var now = new DateTime(2026, 10, 2, 12, 0, 0);

            Assert.False(Titlovi.IsTokenExpired("2026-10-05T12:00:00", now));
            Assert.False(Titlovi.IsTokenExpired("2026-10-03T12:00:01", now));
            Assert.True(Titlovi.IsTokenExpired("2026-10-03T12:00:00", now));
            Assert.True(Titlovi.IsTokenExpired("2026-10-02T18:00:00", now));
            Assert.True(Titlovi.IsTokenExpired("2026-10-01T12:00:00", now));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not a date")]
        public void IsTokenExpired_TreatsUnknownExpirationAsExpired(string? expiration)
        {
            Assert.True(Titlovi.IsTokenExpired(expiration));
        }
    }
}
