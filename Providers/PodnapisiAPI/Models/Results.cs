using System;
using System.Xml.Serialization;

namespace subbuzz.Providers.PodnapisiAPI.Models
{
    [Serializable, XmlRoot("results")]
    public class Results
    {
        [XmlElement("pagination")]
        public Pagination pages;
        [XmlElement("subtitle")]
        public Subtitle[] subtitles;
    }
}
