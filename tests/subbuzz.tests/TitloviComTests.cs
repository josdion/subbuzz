using subbuzz.Providers;
using subbuzz.Providers.TitloviAPI.Models.Responses;
using Xunit;

namespace subbuzz.tests
{
    public class TitloviComTests
    {
        private static SubtitleResult Sub(int? season, int? episode, string title = "The Wire", int? year = 2002)
        {
            return new SubtitleResult { Id = 1, Title = title, Year = year, Season = season, Episode = episode };
        }

        [Theory]
        [InlineData(5, 3, true)]   // exact episode
        [InlineData(5, 0, true)]   // season pack
        [InlineData(0, 0, true)]   // complete pack
        [InlineData(5, 4, false)]  // other episode
        [InlineData(4, 3, false)]  // other season
        [InlineData(null, null, true)]
        public void IsMatchingEpisode_KeepsRequestedEpisodeAndPacks(int? season, int? episode, bool expected)
        {
            Assert.Equal(expected, TitloviCom.IsMatchingEpisode(Sub(season, episode), 5, 3));
        }

        [Fact]
        public void IsMatchingEpisode_WithoutRequestedNumbersKeepsEverything()
        {
            Assert.True(TitloviCom.IsMatchingEpisode(Sub(5, 3), null, null));
            Assert.True(TitloviCom.IsMatchingEpisode(Sub(5, 3), 5, null));
        }

        [Fact]
        public void IsMatchingEpisode_ForSpecialsSkipsRegularSeasons()
        {
            Assert.False(TitloviCom.IsMatchingEpisode(Sub(1, 2), 0, 2));
            Assert.True(TitloviCom.IsMatchingEpisode(Sub(0, 2), 0, 2));
        }

        [Fact]
        public void GetTitle_FormatsMovieAndEpisodeTitles()
        {
            Assert.Equal("Avatar (2009)", TitloviCom.GetTitle(Sub(-1, -1, "Avatar", 2009), false));
            Assert.Equal("Avatar", TitloviCom.GetTitle(Sub(null, null, "Avatar", null), false));
            Assert.Equal("The Wire (2002) S05E03", TitloviCom.GetTitle(Sub(5, 3), true));
            Assert.Equal("The Wire (2002) S05", TitloviCom.GetTitle(Sub(5, 0), true));
            Assert.Equal("The Wire (2002)", TitloviCom.GetTitle(Sub(0, 0), true));
            Assert.Equal("The Wire S05E03", TitloviCom.GetTitle(Sub(5, 3, " The Wire ", null), true));
        }

        [Fact]
        public void GetTitle_WithReleaseIsUnderstoodByTheEpisodeParser()
        {
            string title = TitloviCom.GetTitle(Sub(5, 3), true) + " 720p BluRay x264-DEMAND";

            var info = Parser.Episode.ParseTitle(title);

            Assert.NotNull(info);
            Assert.Equal(5, info.SeasonNumber);
            Assert.Equal(new[] { 3 }, info.EpisodeNumbers);
            Assert.Equal("DEMAND", info.ReleaseGroup);
            Assert.Equal(Parser.Episode.NormalizeTitle("The Wire"), Parser.Episode.NormalizeTitle(info.SeriesTitleInfo.TitleWithoutYear));
            Assert.Equal(2002, info.SeriesTitleInfo.Year);
        }

        [Fact]
        public void GetTitle_WithReleaseIsUnderstoodByTheMovieParser()
        {
            string title = TitloviCom.GetTitle(Sub(-1, -1, "Avatar", 2009), false) + " Extended Collectors Edition UHD BluRay 2160p HDR10+ DV HEVC DTS-HD MA 5.1 x265-E";

            var info = Parser.Movie.ParseTitle(title, false);

            Assert.NotNull(info);
            Assert.Equal(Parser.Movie.NormalizeTitle("Avatar"), Parser.Movie.NormalizeTitle(info.MovieTitle));
            Assert.Equal(2009, info.Year);
        }
    }
}
