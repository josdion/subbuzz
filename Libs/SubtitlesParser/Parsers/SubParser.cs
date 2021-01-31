using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitlesParser.Classes.Parsers
{
    public class SubParser 
    {
        // Properties -----------------------------------------------------------------------
        
        private readonly Dictionary<SubtitlesFormat, ISubtitlesParser> _subFormatToParser = new Dictionary<SubtitlesFormat, ISubtitlesParser>
            {
                {SubtitlesFormat.SubRipFormat, new SrtParser()},
                {SubtitlesFormat.MicroDvdFormat, new MicroDvdParser()},
                {SubtitlesFormat.SubViewerFormat, new SubViewerParser()},
                {SubtitlesFormat.SubStationAlphaFormat, new SsaParser()},
                {SubtitlesFormat.TtmlFormat, new TtmlParser()},
                {SubtitlesFormat.WebVttFormat, new VttParser()},
                {SubtitlesFormat.YoutubeXmlFormat, new YtXmlFormatParser()}
            };


        // Constructors --------------------------------------------------------------------

        public SubParser(){}


        // Methods -------------------------------------------------------------------------

        /// <summary>
        /// Gets the most likely format of the subtitle file based on its filename.
        /// Most likely because .sub are sometimes srt files for example.
        /// </summary>
        /// <param name="fileName">The subtitle file name</param>
        /// <returns>The most likely subtitles format</returns>
        public SubtitlesFormat GetMostLikelyFormat(string fileName)
        {
            var extension = Path.GetExtension(fileName);

            if (!string.IsNullOrEmpty(extension))
            {
                foreach (var format in SubtitlesFormat.SupportedSubtitlesFormats)
                {
                    if (format.Extension != null && Regex.IsMatch(extension, format.Extension, RegexOptions.IgnoreCase))
                    {
                        return format;
                    }
                } 
            }

            return null;
        }

        /// <summary>
        /// Parses a subtitles file stream
        /// </summary>
        /// <param name="stream">The subtitles file stream</param>
        /// <returns>The corresponding list of SubtitleItems</returns>
        public List<SubtitleItem> ParseStream(Stream stream)
        {
            // we default encoding to UTF-8
            return ParseStream(stream, Encoding.UTF8);
        }

        /// <summary>
        /// Parses a subtitle file stream.
        /// We try all the parsers registered in the _subFormatToParser dictionary
        /// </summary>
        /// <param name="stream">The subtitle file stream</param>
        /// <param name="encoding">The stream encoding</param>
        /// <param name="subFormat">The preferred subFormat to try first (if we have a clue with the subtitle file name for example)</param>
        /// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
        public List<SubtitleItem> ParseStream(Stream stream, Encoding encoding, SubtitlesFormat subFormat = null)
        {
            var dictionary = subFormat != null ?
                _subFormatToParser
                // start the parsing by the specified format
                .OrderBy(dic => Math.Abs(string.Compare(dic.Key.Name, subFormat.Name, StringComparison.Ordinal)))
                .ToDictionary(entry => entry.Key, entry => entry.Value):
                _subFormatToParser;

            return ParseStream(stream, encoding, dictionary);
        }

        /// <summary>
        /// Parses a subtitle file stream.
        /// We try all the parsers registered in the _subFormatToParser dictionary
        /// </summary>
        /// <param name="stream">The subtitle file stream</param>
        /// <param name="encoding">The stream encoding</param>
        /// <param name="subFormatDictionary">The dictionary of the subtitles parser (ordered) to try</param>
        /// <returns>The corresponding list of SubtitleItem, null if parsing failed</returns>
        public List<SubtitleItem> ParseStream(Stream stream, Encoding encoding, Dictionary<SubtitlesFormat, ISubtitlesParser> subFormatDictionary)
        {
            // test if stream if readable
            if (!stream.CanRead)
            {
                throw new ArgumentException("Cannot parse a non-readable stream");
            }

            // copy the stream if not seekable
            var seekableStream = stream;
            if (!stream.CanSeek)
            {
                seekableStream = StreamHelpers.CopyStream(stream);
                seekableStream.Seek(0, SeekOrigin.Begin);
            }

            // if dictionary is null, use the default one
            subFormatDictionary = subFormatDictionary ?? _subFormatToParser;

            foreach (var subtitlesParser in subFormatDictionary)
            {
                try
                {
                    var parser = subtitlesParser.Value;
                    var items = parser.ParseStream(seekableStream, encoding);
                    return items;
                }
                catch//(Exception ex)
                {
                    continue; // Let's try the next parser...
                    //Console.WriteLine(ex);
                }
            }

            // all the parsers failed
            var firstCharsOfFile = LogFirstCharactersOfStream(stream, 500, encoding);
            var message = string.Format("All the subtitles parsers failed to parse the following stream:{0}", firstCharsOfFile);
            throw new ArgumentException(message);
        }
        
        /// <summary>
        /// Logs the first characters of a stream for debug
        /// </summary>
        /// <param name="stream">The file stream</param>
        /// <param name="nbOfCharactersToPrint">The number of caracters to print</param>
        /// <param name="encoding">The stream encoding</param>
        /// <returns>The first characters of the stream</returns>
        private string LogFirstCharactersOfStream(Stream stream, int nbOfCharactersToPrint, Encoding encoding)
        {
            var message = "";
            // print the first 500 characters
            if (stream.CanRead)
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                var reader = new StreamReader(stream, encoding, true);

                var buffer = new char[nbOfCharactersToPrint];
                reader.ReadBlock(buffer, 0, nbOfCharactersToPrint);
                message = string.Format("Parsing of subtitle stream failed. Beginning of sub stream:\n{0}",
                                        string.Join("", buffer));
            }
            else
            {
                message = string.Format("Tried to log the first {0} characters of a closed stream",
                                        nbOfCharactersToPrint);
            }
            return message;
        }

    }
}
