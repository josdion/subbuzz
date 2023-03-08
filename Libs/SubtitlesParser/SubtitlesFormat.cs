using System.Collections.Generic;

namespace SubtitlesParser.Classes
{
    public class SubtitlesFormat
    {
        // Properties -----------------------------------------

        public string Name { get; private set; }
        public string Extension { get; private set; }
        public string ExtensionPattern { get; private set; }    


        // Private constructor to avoid duplicates ------------

        private SubtitlesFormat(){}


        // Predefined instances -------------------------------

        public static SubtitlesFormat SubRipFormat = new SubtitlesFormat()
        {
            Name = "SubRip",
            Extension = "srt",
            ExtensionPattern = @"\.srt"
        };
        public static SubtitlesFormat MicroDvdFormat = new SubtitlesFormat()
        {
            Name = "MicroDvd",
            Extension = "sub",
            ExtensionPattern = @"\.sub"
        };
        public static SubtitlesFormat SubViewerFormat = new SubtitlesFormat()
        {
            Name = "SubViewer",
            Extension = "sub",
            ExtensionPattern = @"\.sub"
        };
        public static SubtitlesFormat SubStationAlphaFormat = new SubtitlesFormat()
        {
            Name = "SubStationAlpha",
            Extension = "ssa",
            ExtensionPattern = @"\.(ssa|ass)"
        };
        public static SubtitlesFormat TtmlFormat = new SubtitlesFormat()
        {
            Name = "TTML",
            Extension = "ttml",
            ExtensionPattern = @"\.ttml"
        };
        public static SubtitlesFormat WebVttFormat = new SubtitlesFormat()
        {
            Name = "WebVTT",
            Extension = "vtt",
            ExtensionPattern = @"\.vtt"
        };
        public static SubtitlesFormat YoutubeXmlFormat = new SubtitlesFormat()
        {
            Name = "YoutubeXml",
            Extension = "xml",
            ExtensionPattern = @"\.xml"
        };

        public static List<SubtitlesFormat> SupportedSubtitlesFormats = new List<SubtitlesFormat>()
            {
                SubRipFormat, 
                MicroDvdFormat,
                SubViewerFormat,
                SubStationAlphaFormat,
                TtmlFormat,
                WebVttFormat,
                YoutubeXmlFormat
            };

    }

    
}
