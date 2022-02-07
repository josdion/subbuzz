using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesParser.Classes.Parsers
{
    /// <summary>
    /// Parser for SubViewer .sub subtitles files
    /// 
    /// [INFORMATION]
    /// ....
    /// 
    /// 00:04:35.03,00:04:38.82
    /// Hello guys... please sit down...
    /// 
    /// 00:05:00.19,00:05:03.47
    /// M. Franklin,[br]are you crazy?
    /// 
    /// see https://en.wikipedia.org/wiki/SubViewer
    /// </summary>
    public class SubViewerParser : ISubtitlesParser
    {
        // Properties ----------------------------------------------------------

        private const string FirstLine = "[INFORMATION]";
        private const short MaxLineNumberForItems = 20;

        private readonly Regex _timestampRegex = new Regex(@"\d{2}:\d{2}:\d{2}\.\d{2},\d{2}:\d{2}:\d{2}\.\d{2}", RegexOptions.Compiled);
        private const char TimecodeSeparator = ',';


        // Methods -------------------------------------------------------------

        public List<SubtitleItem> ParseStream(Stream subStream, Encoding encoding)
        {
            // seek the beginning of the stream
            subStream.Position = 0;
            var reader = new StreamReader(subStream, encoding, true);

            var firstLine = reader.ReadLine();
            if (firstLine == FirstLine)
            {
                var line = reader.ReadLine();
                var lineNumber = 2;
                while (line != null && lineNumber <= MaxLineNumberForItems && !IsTimestampLine(line))
                {
                    line = reader.ReadLine();
                    lineNumber++;
                }

                // first relevant line should be a timecode
                if (line != null && lineNumber <= MaxLineNumberForItems && IsTimestampLine(line))
                {
                    // we parse all the lines
                    var items = new List<SubtitleItem>();

                    var timeCodeLine = line;
                    var textLines = new List<string>();

                    while (line != null)
                    {
                        line = reader.ReadLine();
                        if (IsTimestampLine(line))
                        {
                            // store previous item
                            var timeCodes = ParseTimecodeLine(timeCodeLine);
                            var start = timeCodes.Item1;
                            var end = timeCodes.Item2;

                            if (start > 0 && end > 0 && textLines.Any())
                            {
                                items.Add(new SubtitleItem()
                                    {
                                        StartTime = start,
                                        EndTime = end,
                                        Lines = textLines
                                    });
                            }

                            // reset timecode line and text lines
                            timeCodeLine = line;
                            textLines = new List<string>();
                        } else if (!string.IsNullOrEmpty(line))
                        {
                            // it's a text line
                            textLines.AddRange(line.Split(new[] { "[br]" }, StringSplitOptions.RemoveEmptyEntries));
                        }
                    }

                    // store last line if necessary
                    var lastTimeCodes = ParseTimecodeLine(timeCodeLine);
                    var lastStart = lastTimeCodes.Item1;
                    var lastEnd = lastTimeCodes.Item2;
                    if (lastStart > 0 && lastEnd > 0 && textLines.Any())
                    {
                        items.Add(new SubtitleItem()
                            {
                                StartTime = lastStart,
                                EndTime = lastEnd,
                                Lines = textLines
                            });
                    }

                    if (items.Any())
                    {
                        return items;
                    }
                    else
                    {
                        throw new ArgumentException("Stream is not in a valid SubViewer format");
                    }
                }
                else
                {
                    var message = string.Format("Couldn't find the first timestamp line in the current sub file. " +
                                                "Last line read: '{0}', line number #{1}", line, lineNumber);
                    throw new ArgumentException(message);
                }
            }
            else
            {
                throw new ArgumentException("Stream is not in a valid SubViewer format");
            }
        }

        private Tuple<int, int> ParseTimecodeLine(string line)
        {
            var parts = line.Split(TimecodeSeparator);
            if (parts.Length == 2)
            {
                var start = ParseTimecode(parts[0]);
                var end = ParseTimecode(parts[1]);
                return new Tuple<int, int>(start, end);
            }
            else
            {
                var message = string.Format("Couldn't parse the timecodes in line '{0}'", line);
                throw new ArgumentException(message);
            }
        }

        /// <summary>
        /// Takes an SRT timecode as a string and parses it into a double (in seconds). A SRT timecode reads as follows: 
        /// 00:00:20,000
        /// </summary>
        /// <param name="s">The timecode to parse</param>
        /// <returns>The parsed timecode as a TimeSpan instance. If the parsing was unsuccessful, -1 is returned (subtitles should never show)</returns>
        private int ParseTimecode(string s)
        {
            TimeSpan result;

            if (TimeSpan.TryParse(s, out result))
            {
                var nbOfMs = (int)result.TotalMilliseconds;
                return nbOfMs;
            }
            else
            {
                return -1;
            }
        }

        /// <summary>
        /// Tests if the current line is a timestamp line
        /// </summary>
        /// <param name="line">The subtitle file line</param>
        /// <returns>True if it's a timestamp line, false otherwise</returns>
        private bool IsTimestampLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }
            var isMatch = _timestampRegex.IsMatch(line);
            return isMatch;
        }
    }
}
