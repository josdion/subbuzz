using subbuzz.Parser.Qualities;
using System.Collections.Generic;

namespace subbuzz.Parser
{
    public class MovieInfo
    {
        public string MovieTitle { get; set; }
        public string OriginalTitle { get; set; }
        public string ReleaseTitle { get; set; }
        public string SimpleReleaseTitle { get; set; }
        public QualityModel Quality { get; set; }
        //public List<Language> Languages { get; set; } = new List<Language>();
        public string ReleaseGroup { get; set; }
        public string ReleaseHash { get; set; }
        public string Edition { get; set; }
        public int Year { get; set; }
        public string ImdbId { get; set; }
        public int TmdbId { get; set; }
        public Dictionary<string, object> ExtraInfo { get; set; } = new Dictionary<string, object>();

        public override string ToString()
        {
            return string.Format("{0} - {1} {2}", MovieTitle, Year, Quality);
        }
    }
}
