using System;
using System.Collections.Generic;

namespace subbuzz.Helpers
{

    public class SubtitleScore : ICloneable
    {
        private static Dictionary<string, int> EpisodeScores = new Dictionary<string, int>
        {
            { "title", 120 },
            { "year", 60 },
            { "season", 20 },
            { "episode", 20 },
            { "release_group", 15 },
            { "source", 8 },
            { "frame_rate", 7 },
            { "audio_codec", 3 },
            { "resolution", 2 },
            { "video_codec", 2 },
        };

        private static Dictionary<string, int> MovieScores = new Dictionary<string, int>
        {
            { "title", 60 },
            { "year", 30 },
            { "edition", 20 },
            { "release_group", 15 },
            { "source", 8 },
            { "frame_rate", 7 },
            { "audio_codec", 3 },
            { "resolution", 2 },
            { "video_codec", 2 },
            { "source_modifier", 1 },
        };

        private HashSet<String> Matches;
        
        public SubtitleScore()
        {
            Matches = new HashSet<String>();
        }

        public SubtitleScore(HashSet<String> matches)
        {
            Matches = new HashSet<String>(matches);
        }

        public object Clone()
        {
            return new SubtitleScore(Matches);
        }

        public bool AddMatch(String match)
        {
            switch (match)
            {
                case "imdb":
                    Matches.Add("title");
                    Matches.Add("year");
                    break;

                case "imdb_episode":
                    Matches.Add("title");
                    Matches.Add("year");
                    Matches.Add("season");
                    Matches.Add("episode");
                    break;
            }

            return Matches.Add(match);
        }

        private float CalcScore(Dictionary<string, int> scores)
        {
            float max_score = 0;
            float score = 0;

            foreach (var s in scores.Values)
            {
                max_score += s;
            }

            if (max_score == 0) return 0;

            foreach (var m in Matches)
            {
                if (m == "hash")
                {
                    score = max_score;
                    break;
                }

                score += scores.ContainsKey(m) ? scores[m] : 0;
            }

            return (score * 100) / max_score;
        }

        public float CalcScoreEpisode()
        {
            return CalcScore(EpisodeScores);
        }

        public float CalcScoreMovie()
        {
            return CalcScore(MovieScores);
        }
    }

}
