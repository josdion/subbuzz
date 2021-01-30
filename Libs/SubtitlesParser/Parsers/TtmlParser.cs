using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SubtitlesParser.Classes.Parsers
{
    public class TtmlParser : ISubtitlesParser
    {
        public List<SubtitleItem> ParseStream(Stream xmlStream, Encoding encoding)
        {
            // rewind the stream
            xmlStream.Position = 0;
            var items = new List<SubtitleItem>();

            // parse xml stream
            var xElement = XElement.Load(xmlStream);
            var tt = xElement.GetNamespaceOfPrefix("tt") ?? xElement.GetDefaultNamespace();

            var nodeList = xElement.Descendants(tt + "p").ToList();
            foreach (var node in nodeList)
            {
                try
                {
                    var reader = node.CreateReader();
                    reader.MoveToContent();
                    var beginString = node.Attribute("begin").Value.Replace("t", "");
                    var startTicks = ParseTimecode(beginString);
                    var endString = node.Attribute("end").Value.Replace("t", "");
                    var endTicks = ParseTimecode(endString);
                    var text = reader.ReadInnerXml()
                        .Replace("<tt:", "<")
                        .Replace("</tt:", "</")
                        .Replace(string.Format(@" xmlns:tt=""{0}""", tt), "")
                        .Replace(string.Format(@" xmlns=""{0}""", tt), "");

                    items.Add(new SubtitleItem()
                    {
                        StartTime = (int)(startTicks),
                        EndTime = (int)(endTicks),
                        Lines = new List<string>() { text }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception raised when parsing xml node {0}: {1}", node, ex);
                }
            }

            if (items.Any())
            {
                return items;
            }
            throw new ArgumentException("Stream is not in a valid TTML format, or represents empty subtitles");
        }

        /// <summary>
        /// Takes an SRT timecode as a string and parses it into a double (in seconds). A SRT timecode reads as follows: 
        /// 00:00:20,000
        /// </summary>
        /// <param name="s">The timecode to parse</param>
        /// <returns>The parsed timecode as a TimeSpan instance. If the parsing was unsuccessful, -1 is returned (subtitles should never show)</returns>
        private static long ParseTimecode(string s)
        {
            TimeSpan result;
            if (TimeSpan.TryParse(s, out result))
            {
                return (long) result.TotalMilliseconds;
            }
            // Netflix subtitles have a weird format: timecodes are specified as ticks. Ex: begin="79249170t"
            long ticks;
            if (long.TryParse(s.TrimEnd('t'), out ticks))
            {
                return ticks/10000;
            }
            return -1;
        }
    }
}