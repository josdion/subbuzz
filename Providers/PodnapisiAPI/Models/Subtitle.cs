using System.Xml.Serialization;

namespace subbuzz.Providers.PodnapisiAPI.Models
{
    public class ExternalMovieIdentifiers
    {
        [XmlElement("imdb")]
        public string imdb;
        [XmlElement("omdb")]
        public string omdb;
    }

    public class Release
    {
        [XmlElement("release")]
        public string[] releases;
    }

    // grammer_correct (p)
    // lyrics
    // hearing_impaired (n)
    // high_definition (h)
    public class NewFlags
    {
        [XmlElement("flag")]
        public string[] flag;
    }

    public class Subtitle
    {
        [XmlElement("pid")]
        public string pid;
        [XmlElement("title")]
        public string title;
        [XmlElement("year")]
        public string year;
        [XmlElement("externalMovieIdentifiers")]
        public ExternalMovieIdentifiers externalMovieId;
        [XmlElement("url")]
        public string url;
        [XmlElement("uploaderName")]
        public string uploaderName;
        [XmlElement("release")]
        public string release;
        [XmlElement("releases")]
        public Release releases;
        [XmlElement("language")]
        public string language;
        [XmlElement("time")]
        public int timestamp;
        [XmlElement("tvSeason")]
        public int tvSeason;
        [XmlElement("tvEpisode")]
        public int tvEpisode;
        [XmlElement("tvSpecial")]
        public int tvSpecial;
        [XmlElement("cds")]
        public string cds;
        [XmlElement("format")]
        public string format;
        [XmlElement("fps")]
        public string fps;
        [XmlElement("rating")]
        public float rating;
        [XmlElement("flags")]
        public string flags;
        [XmlElement("new_flags")]
        public NewFlags new_flags;
        [XmlElement("downloads")]
        public int downloads;
    }
}
