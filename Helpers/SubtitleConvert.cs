using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SubtitlesParser.Classes;
using SubtitlesParser.Classes.Parsers;

namespace subbuzz.Helpers
{
    class SubtitleConvert
    {
        public static Stream ToSrt(Stream ins, Encoding encoding, bool convertToUtf8, float fps)
        {
            var outs = new MemoryStream();
            var writer = new StreamWriter(outs, convertToUtf8 ? Encoding.UTF8 : encoding);

            Dictionary<SubtitlesFormat, ISubtitlesParser> parsers = new Dictionary<SubtitlesFormat, ISubtitlesParser>
            {
                {SubtitlesFormat.SubRipFormat, new SrtParser()},
                {SubtitlesFormat.MicroDvdFormat, new MicroDvdParser(fps)},
                {SubtitlesFormat.SubViewerFormat, new SubViewerParser()},
                {SubtitlesFormat.SubStationAlphaFormat, new SsaParser()},
                {SubtitlesFormat.TtmlFormat, new TtmlParser()},
                {SubtitlesFormat.WebVttFormat, new VttParser()},
                {SubtitlesFormat.YoutubeXmlFormat, new YtXmlFormatParser()}
            };

            try
            {
                var parser = new SubParser();
                List<SubtitleItem> items = parser.ParseStream(ins, encoding, parsers);

                for (int i = 0; i < items.Count; i++)
                {
                    var start = TimeSpan.FromMilliseconds(items[i].StartTime);
                    var end = TimeSpan.FromMilliseconds(items[i].EndTime);

                    string line = String.Format("{0}\n{1} --> {2}\n{3}\n\n", 
                        i+1, 
                        start.ToString(@"hh\:mm\:ss\,fff"), 
                        end.ToString(@"hh\:mm\:ss\,fff"), 
                        string.Join("\n", items[i].Lines));
                    
                    writer.Write(line);
                }

                writer.Flush();
                outs.Seek(0, SeekOrigin.Begin);
            }
            catch
            {
            }

            return outs;
        }
    }
}
