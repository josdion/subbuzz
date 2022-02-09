using System.Xml.Serialization;

namespace subbuzz.Providers.PodnapisiAPI.Models
{
    public class Pagination
    {
        [XmlElement("current")]
        public int current;
        [XmlElement("count")]
        public int count;
        [XmlElement("results")]
        public int results;
    }
}
