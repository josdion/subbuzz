using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SubtitlesParser.Classes.Utils;

namespace SubtitlesParser.Classes.Parsers
{
    /// <summary>
    /// A parser for the SubStation Alpha subtitles format.
    /// See http://en.wikipedia.org/wiki/SubStation_Alpha for complete explanations.
    /// Ex:
    /// [Script Info]
    /// ; This is a Sub Station Alpha v4 script.
    /// ; For Sub Station Alpha info and downloads,
    /// ; go to http://www.eswat.demon.co.uk/
    /// Title: Neon Genesis Evangelion - Episode 26 (neutral Spanish)
    /// Original Script: RoRo
    /// Script Updated By: version 2.8.01
    /// ScriptType: v4.00
    /// Collisions: Normal
    /// PlayResY: 600
    /// PlayDepth: 0
    /// Timer: 100,0000
    ///  
    /// [V4 Styles]
    /// Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, TertiaryColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, AlphaLevel, Encoding
    /// Style: DefaultVCD, Arial,28,11861244,11861244,11861244,-2147483640,-1,0,1,1,2,2,30,30,30,0,0
    ///   
    /// [Events]
    /// Format: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
    /// Dialogue: Marked=0,0:00:01.18,0:00:06.85,DefaultVCD, NTP,0000,0000,0000,,{\pos(400,570)}Like an angel with pity on nobody
    /// </summary>
    public class SsaParser : ISubtitlesParser
    {

        // Methods ------------------------------------------------------------------

        public List<SubtitleItem> ParseStream(Stream ssaStream, Encoding encoding)
        {
            // test if stream if readable and seekable (just a check, should be good)
            if (!ssaStream.CanRead || !ssaStream.CanSeek)
            {
                var message = string.Format("Stream must be seekable and readable in a subtitles parser. " +
                                            "Operation interrupted; isSeekable: {0} - isReadable: {1}",
                                            ssaStream.CanSeek, ssaStream.CanRead);
                throw new ArgumentException(message);
            }

            // seek the beginning of the stream
            ssaStream.Position = 0;

            var reader = new StreamReader(ssaStream, encoding, true);

            // default wrap style to none if the header section doesn't contain a wrap style definition (very possible since it wasn't present in SSA, only ASS) 
            SsaWrapStyle wrapStyle = SsaWrapStyle.None;

            var line = reader.ReadLine();
            var lineNumber = 1;
            // read the line until the [Events] section
            while (line != null && line != SsaFormatConstants.EVENT_LINE)
            {
                if (line.StartsWith(SsaFormatConstants.WRAP_STYLE_PREFIX))
                {
                    // get the wrap style
                    // the raw string is the second array item after splitting the line at `:` (which we know will be present since it's
                    // included in the `WRAP_STYLE_PREFIX` const), so trim the space off the beginning of that item, and parse that string into the enum 
                    wrapStyle = line.Split(':')[1].TrimStart().FromString();
                }

                line = reader.ReadLine();
                lineNumber++;
            }

            if (line != null)
            {
                // we are at the event section
                var headerLine = reader.ReadLine();
                if (!string.IsNullOrEmpty(headerLine))
                {
                    var columnHeaders = headerLine.Split(SsaFormatConstants.SEPARATOR).Select(head => head.Trim()).ToList();

                    var startIndexColumn = columnHeaders.IndexOf(SsaFormatConstants.START_COLUMN);
                    var endIndexColumn = columnHeaders.IndexOf(SsaFormatConstants.END_COLUMN);
                    var textIndexColumn = columnHeaders.IndexOf(SsaFormatConstants.TEXT_COLUMN);

                    if (startIndexColumn > 0 && endIndexColumn > 0 && textIndexColumn > 0)
                    {
                        var items = new List<SubtitleItem>();

                        line = reader.ReadLine();
                        while (line != null)
                        {
                            if (!string.IsNullOrEmpty(line))
                            {
                                var columns = line.Split(SsaFormatConstants.SEPARATOR);
                                var startText = columns[startIndexColumn];
                                var endText = columns[endIndexColumn];


                                var textLine = string.Join(",", columns.Skip(textIndexColumn));

                                var start = ParseSsaTimecode(startText);
                                var end = ParseSsaTimecode(endText);

                                if (start > 0 && end > 0 && !string.IsNullOrEmpty(textLine))
                                {
                                    List<string> lines;
                                    switch (wrapStyle)
                                    {
                                        case SsaWrapStyle.Smart:
                                        case SsaWrapStyle.SmartWideLowerLine:
                                        case SsaWrapStyle.EndOfLine:
                                            // according to the spec doc: 
                                            // `\n` is ignored by SSA if smart-wrapping (and therefore smart with wider lower line) is enabled
                                            // end-of-line word wrapping: only `\N` breaks
                                            lines = textLine.Split(new[] { @"\N" }, StringSplitOptions.None).ToList();
                                            break;
                                        case SsaWrapStyle.None:
                                            // the default value of the variable is None, which breaks on either `\n` or `\N`

                                            // according to the spec doc: 
                                            // no word wrapping: `\n` `\N` both breaks
                                            lines = Regex.Split(textLine, @"(?:\\n)|(?:\\N)").ToList(); // regex because there isn't an overload to take an array of strings to split on
                                            break;
                                        default:
                                            throw new ArgumentOutOfRangeException();
                                    }
                                    
                                    // trim any spaces from the start of a line (happens when a subtitler includes a space after a newline char ie `this is\N two lines` instead of `this is\Ntwo lines`)
                                    // this doesn't actually matter for the SSA/ASS format, however if you were to want to convert from SSA/ASS to a format like SRT, it could lead to spaces preceding the second line, which looks funny 
                                    lines = lines.Select(l => l.TrimStart()).ToList();

                                    var item = new SubtitleItem()
                                    {
                                        StartTime = start,
                                        EndTime = end,
                                        Lines = lines,
                                        // strip formatting by removing anything within curly braces, this will not remove duplicate content however, which can happen when working with signs for example
                                        PlaintextLines = lines.Select(subtitleLine => Regex.Replace(subtitleLine, @"\{.*?\}", string.Empty)).ToList()
                                    };
                                    items.Add(item);
                                }
                            }
                            line = reader.ReadLine();
                        }

                        if (items.Any())
                        {
                            return items;
                        }
                        else
                        {
                            throw new ArgumentException("Stream is not in a valid Ssa format");
                        }
                    }
                    else
                    {
                        var message = string.Format("Couldn't find all the necessary columns " +
                                                    "headers ({0}, {1}, {2}) in header line {3}",
                                                    SsaFormatConstants.START_COLUMN, SsaFormatConstants.END_COLUMN,
                                                    SsaFormatConstants.TEXT_COLUMN, headerLine);
                        throw new ArgumentException(message);
                    }
                }
                else
                {
                    var message = string.Format("The header line after the line '{0}' was null -> " +
                                                "no need to continue parsing", line);
                    throw new ArgumentException(message);
                }
            }
            else
            {
                var message = string.Format("We reached line '{0}' with line number #{1} without finding to " +
                                            "Event section ({2})", line, lineNumber, SsaFormatConstants.EVENT_LINE);
                throw new ArgumentException(message);
            }
        }

        /// <summary>
        /// Takes an SRT timecode as a string and parses it into a double (in seconds). A SRT timecode reads as follows: 
        /// 00:00:20,000
        /// </summary>
        /// <param name="s">The timecode to parse</param>
        /// <returns>The parsed timecode as a TimeSpan instance. If the parsing was unsuccessful, -1 is returned (subtitles should never show)</returns>
        private int ParseSsaTimecode(string s)
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
    }
}
