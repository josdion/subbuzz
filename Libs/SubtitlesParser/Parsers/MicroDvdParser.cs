using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesParser.Classes.Parsers
{
    /// <summary>
    /// Parser for MicroDVD .sub subtitles files
    /// 
    /// A .sub file looks like this:
    /// {1}{1}29.970
    /// {0}{180}PIRATES OF THE CARIBBEAN|English subtitlez by tHe.b0dY
    /// {509}{629}Drink up me 'earties yo ho!
    /// {635}{755}We kidnap and ravage and don't give a hoot.
    /// 
    /// We need the video frame rate to extract .sub files -> careful when using it
    /// 
    /// see https://en.wikipedia.org/wiki/MicroDVD
    /// </summary>
    public class MicroDvdParser : ISubtitlesParser
    {
        // Properties -----------------------------------------------------------------------

        private readonly float defaultFrameRate = 25;
        private readonly char[] _lineSeparators = {'|'};


        // Constructors --------------------------------------------------------------------

        public MicroDvdParser(){}

        public MicroDvdParser(float defaultFrameRate)
        {
            this.defaultFrameRate = defaultFrameRate;
        }


        // Methods -------------------------------------------------------------------------

        public List<SubtitleItem> ParseStream(Stream subStream, Encoding encoding)
        {
            // test if stream if readable and seekable (just a check, should be good)
            if (!subStream.CanRead || !subStream.CanSeek)
            {
                var message = string.Format("Stream must be seekable and readable in a subtitles parser. " +
                                   "Operation interrupted; isSeekable: {0} - isReadable: {1}", 
                                   subStream.CanSeek, subStream.CanSeek);
                throw new ArgumentException(message);
            }

            // seek the beginning of the stream
            subStream.Position = 0;
            var reader = new StreamReader(subStream, encoding, true);

            var items = new List<SubtitleItem>();
            var line = reader.ReadLine();
            // find the first relevant line
            while (line != null && !IsMicroDvdLine(line))
            {
                line = reader.ReadLine();
            }

            if (line != null)
            {
                float frameRate;
                // try to extract the framerate from the first line
                var firstItem = ParseLine(line, defaultFrameRate);
                if (firstItem.Lines != null && firstItem.Lines.Any())
                {
                    var success = TryExtractFrameRate(firstItem.Lines[0], out frameRate);
                    if (!success)
                    {
                        Console.WriteLine("Couldn't extract frame rate of sub file with first line {0}. " +
                                          "We use the default frame rate: {1}", line, defaultFrameRate);
                        frameRate = defaultFrameRate;

                        // treat it as a regular line
                        items.Add(firstItem);
                    }
                }
                else
                {
                    frameRate = defaultFrameRate;
                }

                // parse other lines
                line = reader.ReadLine();
                while (line != null)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        var item = ParseLine(line, frameRate);
                        items.Add(item); 
                    }
                    line = reader.ReadLine();
                }
            }

            if (items.Any())
            {
                return items;
            }
            else
            {
                throw new ArgumentException("Stream is not in a valid MicroDVD format");
            }
        }

        private const string LineRegex = @"^[{\[](-?\d+)[}\]][{\[](-?\d+)[}\]](.*)";

        private bool IsMicroDvdLine(string line)
        {
            return Regex.IsMatch(line, LineRegex);
        }

        /// <summary>
        /// Parses one line of the .sub file
        /// 
        /// ex:
        /// {0}{180}PIRATES OF THE CARIBBEAN|English subtitlez by tHe.b0dY
        /// </summary>
        /// <param name="line">The .sub file line</param>
        /// <param name="frameRate">The frame rate with which the .sub file was created</param>
        /// <returns>The corresponding SubtitleItem</returns>
        private SubtitleItem ParseLine(string line, float frameRate)
        {
            var match = Regex.Match(line, LineRegex);
            if (match.Success && match.Groups.Count > 2)
            {
                var startFrame = match.Groups[1].Value;
                var start = (int)Math.Round(1000 * double.Parse(startFrame) / frameRate);
                var endTime = match.Groups[2].Value;
                var end = (int)Math.Round(1000 * double.Parse(endTime) / frameRate);
                var text = match.Groups[match.Groups.Count - 1].Value;
                var lines = text.Split(_lineSeparators);
                var nonEmptyLines = lines.Where(l => !string.IsNullOrEmpty(l)).ToList();
                var item = new SubtitleItem
                    {
                        Lines = nonEmptyLines,
                        StartTime = start,
                        EndTime = end
                    };

                return item;
            }
            else
            {
                var message = string.Format("The subtitle file line {0} is " +
                                            "not in the micro dvd format. We stop the process.", line);
                throw new InvalidDataException(message);
            }
        }

        /// <summary>
        /// Tries to extract the frame rate from a subtitle file line.
        /// 
        /// Supported formats are:
        /// - {x}{y}25
        /// - {x}{y}{...}23.976
        /// </summary>
        /// <param name="text">The subtitle file line</param>
        /// <param name="frameRate">The frame rate if we can parse it</param>
        /// <returns>True if the parsing was successful, false otherwise</returns>
        private bool TryExtractFrameRate(string text, out float frameRate)
        {
            if (!string.IsNullOrEmpty(text))
            {
                var success = float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                                             out frameRate);
                return success;
            }
            else
            {
                frameRate = defaultFrameRate;
                return false;
            }
        }

    }
}