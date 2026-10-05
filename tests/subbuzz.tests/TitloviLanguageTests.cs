using subbuzz.Providers.TitloviAPI;
using Xunit;

namespace subbuzz.tests
{
    public class TitloviLanguageTests
    {
        [Theory]
        [InlineData("hr")]
        [InlineData("sr")]
        [InlineData("bs")]
        [InlineData("sl")]
        [InlineData("mk")]
        [InlineData("en")]
        [InlineData("HR")]
        [InlineData(" hr ")]
        public void IsLanguageSupported_ReturnsTrueForLanguagesOnTheSite(string? lang)
        {
            Assert.True(Titlovi.IsLanguageSupported(lang));
        }

        [Theory]
        [InlineData("de")]
        [InlineData("bg")]
        [InlineData("")]
        [InlineData(null)]
        public void IsLanguageSupported_ReturnsFalseForOtherLanguages(string? lang)
        {
            Assert.False(Titlovi.IsLanguageSupported(lang));
        }

        [Theory]
        [InlineData("hr", "Hrvatski")]
        [InlineData("sr", "Srpski|Cirilica")]
        [InlineData("bs", "Bosanski")]
        [InlineData("sl", "Slovenski")]
        [InlineData("mk", "Makedonski")]
        [InlineData("en", "English")]
        [InlineData("de", "")]
        [InlineData(null, "")]
        public void GetSearchLanguages_MapsIsoCodeToSiteLanguageNames(string? lang, string expected)
        {
            Assert.Equal(expected, Titlovi.GetSearchLanguages(lang));
        }

        [Theory]
        [InlineData("Hrvatski", "hr")]
        [InlineData("hrvatski", "hr")]
        [InlineData(" English ", "en")]
        [InlineData("Srpski", "sr")]
        [InlineData("Cirilica", "sr")]
        [InlineData("Bosanski", "bs")]
        [InlineData("Slovenski", "sl")]
        [InlineData("Makedonski", "mk")]
        [InlineData("Deutsch", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void GetLanguageCode_MapsSiteLanguageNameToIsoCode(string? titloviLang, string? expected)
        {
            Assert.Equal(expected, Titlovi.GetLanguageCode(titloviLang));
        }
    }
}
