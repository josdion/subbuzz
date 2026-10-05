using subbuzz.Providers.TitloviAPI;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace subbuzz.tests
{
    public class TitloviSearchOptionsTests
    {
        private static string[] Values(List<KeyValuePair<string, string>> options, string key)
        {
            return options.Where(o => o.Key == key).Select(o => o.Value).OrderBy(v => v).ToArray();
        }

        [Fact]
        public void BuildSearchOptions_Movie_UsesTitleImdbAndMovieType()
        {
            var options = Titlovi.BuildSearchOptions("Avatar", "hr", false, null, null, "tt0499549", 1);

            Assert.Equal(new[] { "Avatar" }, Values(options, "query"));
            Assert.Equal(new[] { "Hrvatski" }, Values(options, "lang"));
            Assert.Equal(new[] { "1" }, Values(options, "type"));
            Assert.Equal(new[] { "true" }, Values(options, "json"));
            Assert.Equal(new[] { "tt0499549" }, Values(options, "imdbId"));
            Assert.Empty(Values(options, "season"));
            Assert.Empty(Values(options, "episode"));
            Assert.Empty(Values(options, "pg"));
        }

        [Fact]
        public void BuildSearchOptions_Movie_IgnoresSeasonAndEpisode()
        {
            var options = Titlovi.BuildSearchOptions("Avatar", "hr", false, 1, 2, "", 1);

            Assert.Empty(Values(options, "season"));
            Assert.Empty(Values(options, "episode"));
            Assert.Empty(Values(options, "imdbId"));
        }

        [Fact]
        public void BuildSearchOptions_Episode_AsksForTheEpisodeAndTheSeasonPack()
        {
            var options = Titlovi.BuildSearchOptions("The Wire", "sr", true, 5, 3, null, 1);

            Assert.Equal(new[] { "The Wire" }, Values(options, "query"));
            Assert.Equal(new[] { "Srpski|Cirilica" }, Values(options, "lang"));
            Assert.Equal(new[] { "2" }, Values(options, "type"));
            Assert.Equal(new[] { "5" }, Values(options, "season"));
            Assert.Equal(new[] { "0", "3" }, Values(options, "episode"));
        }

        [Fact]
        public void BuildSearchOptions_SeasonPackRequest_DoesNotDuplicateEpisodeZero()
        {
            var options = Titlovi.BuildSearchOptions("The Wire", "sr", true, 5, 0, null, 1);

            Assert.Equal(new[] { "0" }, Values(options, "episode"));
        }

        [Fact]
        public void BuildSearchOptions_Episode_WithoutNumbersSendsOnlyTheTitle()
        {
            var options = Titlovi.BuildSearchOptions("The Wire", "sr", true, null, null, null, 1);

            Assert.Empty(Values(options, "season"));
            Assert.Empty(Values(options, "episode"));
        }

        [Theory]
        [InlineData(1, new string[0])]
        [InlineData(2, new[] { "2" })]
        [InlineData(7, new[] { "7" })]
        public void BuildSearchOptions_AddsPageOnlyAfterTheFirstOne(int page, string[] expected)
        {
            var options = Titlovi.BuildSearchOptions("Avatar", "hr", false, null, null, null, page);

            Assert.Equal(expected, Values(options, "pg"));
        }

        [Fact]
        public void BuildQueryString_SortsEncodesAndKeepsRepeatedKeys()
        {
            var options = Titlovi.BuildSearchOptions("The Wire", "sr", true, 5, 3, null, 1);

            string url = Titlovi.BuildQueryString("/search", options);

            Assert.Equal("/search?episode=0&episode=3&json=true&lang=Srpski%7cCirilica&query=The+Wire&season=5&type=2", url);
        }

        [Fact]
        public void BuildQueryString_SkipsNullValuesAndReturnsPathWhenEmpty()
        {
            var options = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("a", null),
            };

            Assert.Equal("/search", Titlovi.BuildQueryString("/search", options));
            Assert.Equal("/search", Titlovi.BuildQueryString("/search", new List<KeyValuePair<string, string>>()));
        }

        [Fact]
        public void BuildQueryString_IsStableRegardlessOfInputOrder()
        {
            var first = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("query", "Avatar"),
                new KeyValuePair<string, string>("episode", "3"),
                new KeyValuePair<string, string>("episode", "0"),
            };
            var second = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("episode", "0"),
                new KeyValuePair<string, string>("query", "Avatar"),
                new KeyValuePair<string, string>("episode", "3"),
            };

            Assert.Equal(Titlovi.BuildQueryString("search", first), Titlovi.BuildQueryString("search", second));
        }
    }
}
