using subbuzz.Extensions;
using subbuzz.Parser.Qualities;
using System;
using System.Linq;

namespace subbuzz.Parser
{
    public class EpisodeInfo
    {
        public string ReleaseTitle { get; set; }
        public string SeriesTitle { get; set; }
        public SeriesTitleInfo SeriesTitleInfo { get; set; }
        public QualityModel Quality { get; set; }
        public int SeasonNumber { get; set; }
        public int[] EpisodeNumbers { get; set; }
        public int[] AbsoluteEpisodeNumbers { get; set; }
        public decimal[] SpecialAbsoluteEpisodeNumbers { get; set; }
        public string AirDate { get; set; }
        //public Language Language { get; set; }
        public bool FullSeason { get; set; }
        public bool IsPartialSeason { get; set; }
        public bool IsMultiSeason { get; set; }
        public bool IsSeasonExtra { get; set; }
        public bool Special { get; set; }
        public string ReleaseGroup { get; set; }
        public string ReleaseHash { get; set; }
        public int SeasonPart { get; set; }
        public string ReleaseTokens { get; set; }
        public int? DailyPart { get; set; }

        public EpisodeInfo()
        {
            EpisodeNumbers = new int[0];
            AbsoluteEpisodeNumbers = new int[0];
            SpecialAbsoluteEpisodeNumbers = new decimal[0];
        }

        public bool IsDaily
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AirDate);
            }

            //This prevents manually downloading a release from blowing up in mono
            //TODO: Is there a better way?
            private set { }
        }

        public bool IsAbsoluteNumbering
        {
            get
            {
                return AbsoluteEpisodeNumbers.Any();
            }

            //This prevents manually downloading a release from blowing up in mono
            //TODO: Is there a better way?
            private set { }
        }

        public bool IsPossibleSpecialEpisode
        {
            get
            {
                // if we don't have any episode numbers we are likely a special episode and need to do a search by episode title
                return (AirDate.IsNullOrWhiteSpace() &&
                       SeriesTitle.IsNullOrWhiteSpace() &&
                       (EpisodeNumbers.Length == 0 || SeasonNumber == 0) || !SeriesTitle.IsNullOrWhiteSpace() && Special) ||
                       EpisodeNumbers.Length == 1 && EpisodeNumbers[0] == 0;
            }

            //This prevents manually downloading a release from blowing up in mono
            //TODO: Is there a better way?
            private set { }
        }
        public bool IsPossibleSceneSeasonSpecial
        {
            get
            {
                // SxxE00 episodes
                return SeasonNumber != 0 && EpisodeNumbers.Length == 1 && EpisodeNumbers[0] == 0;
            }

            //This prevents manually downloading a release from blowing up in mono
            //TODO: Is there a better way?
            private set { }
        }

        public override string ToString()
        {
            string episodeString = "[Unknown Episode]";

            if (IsDaily && (EpisodeNumbers == null || EpisodeNumbers.Length == 0))
            {
                episodeString = string.Format("{0}", AirDate);
            }
            else if (FullSeason)
            {
                episodeString = string.Format("Season {0:00}", SeasonNumber);
            }
            else if (EpisodeNumbers != null && EpisodeNumbers.Any())
            {
                episodeString = string.Format("S{0:00}E{1}", SeasonNumber, string.Join("-", EpisodeNumbers.Select(c => c.ToString("00"))));
            }
            else if (AbsoluteEpisodeNumbers != null && AbsoluteEpisodeNumbers.Any())
            {
                episodeString = string.Format("{0}", string.Join("-", AbsoluteEpisodeNumbers.Select(c => c.ToString("000"))));
            }
            else if (Special)
            {
                if (SeasonNumber != 0)
                    episodeString = string.Format("[Unknown Season {0:00} Special]", SeasonNumber);
                else
                    episodeString = "[Unknown Special]";
            }

            return string.Format("{0} - {1} {2}", SeriesTitle, episodeString, Quality);
        }
    }
}
