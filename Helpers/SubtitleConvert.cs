using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using subbuzz.Extensions;
using SubtitlesParser.Classes;
using SubtitlesParser.Classes.Parsers;
using SubtitlesParser.Classes.Writers;

namespace subbuzz.Helpers
{
    public class Subtitle
    {
        static int SubtitleMinimumDisplayMilliseconds = 1000;
        static int SubtitleMaximumDisplayMilliseconds = 8000;
        static int MinimumMillisecondsBetweenLines = 24;

        public SubtitlesFormat Format { get; private set; }
        public List<SubtitleItem> Paragraphs { get; private set; }
        public Encoding Encoding {get; private set; }
        public float? DetectedFrameRate { get; private set; } = null;

        public static Subtitle Load(Stream inStream, SubEncodingCfg encodingCfg, float fps)
        {
            Encoding encoding = encodingCfg.Get();
            if (encodingCfg.AutoDetectEncoding)
            {
                UtfUnknown.DetectionResult csDetect = UtfUnknown.CharsetDetector.DetectFromStream(inStream);
                if (csDetect.Detected != null && csDetect.Detected.Confidence > 0.65)
                    encoding = csDetect.Detected.Encoding ?? encoding;
            }

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

            foreach (var parser in parsers)
            {
                try
                {
                    Subtitle sub = new Subtitle();
                    sub.Paragraphs = parser.Value.ParseStream(inStream, encoding);
                    sub.Format = parser.Key;
                    sub.Encoding = encoding;

                    if (sub.Format == SubtitlesFormat.MicroDvdFormat)
                        sub.DetectedFrameRate = ((MicroDvdParser)parser.Value).DetectedFrameRate;

                    return sub;
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        public void WriteStream(Stream stream, bool encodeToUTF8, out string format)
        {
            if (Format == SubtitlesFormat.SubStationAlphaFormat)
            {
                var writer = new SsaWriter();
                writer.WriteStream(stream, encodeToUTF8 ? Encoding.UTF8 : Encoding, Paragraphs, true);
                format = "ssa";
            }
            else
            {
                // convert to srt
                var writer = new SrtWriter();
                writer.WriteStream(stream, encodeToUTF8 ? Encoding.UTF8 : Encoding, Paragraphs, Format == SubtitlesFormat.SubRipFormat);
                format = "srt";
            }
        }

        public void RemoveStyleTags()
        {
            foreach (var p in Paragraphs)
            {
                if (p.PlaintextLines.Count > 0)
                    p.Lines = p.PlaintextLines;
            }
        }

        public void ChangeFrameRate(double oldFrameRate, double newFrameRate)
        {
            var factor = GetFrameForCalculation(oldFrameRate) / GetFrameForCalculation(newFrameRate);
            foreach (var p in Paragraphs)
            {
                p.StartTime = (int)Math.Round(p.StartTime * factor, MidpointRounding.AwayFromZero);
                p.EndTime = (int)Math.Round(p.EndTime * factor, MidpointRounding.AwayFromZero);
            }
        }

        public void AdjustDuration(double charPerSec, bool extendOnly = false, bool onlyOptimal = false)
        {
            for (var index = 0; index < Paragraphs.Count; index++)
            {
                var item = Paragraphs[index];
                var originalEndTime = item.EndTime;

                var duration = GetOptimalDisplayMilliseconds(item.Plaintext, charPerSec, onlyOptimal);
                item.EndTime = item.StartTime + duration;

                if (extendOnly && item.EndTime < originalEndTime)
                {
                    item.EndTime = originalEndTime;
                    continue;
                }

                var next = (index + 1) < Paragraphs.Count ? Paragraphs[index + 1] : null;
                if (next != null && item.EndTime + MinimumMillisecondsBetweenLines > next.StartTime)
                {
                    item.EndTime = next.StartTime - MinimumMillisecondsBetweenLines;
                    if (item.Duration <= 0)
                    {
                        item.EndTime = item.StartTime + 1;
                    }
                }
            }
        }

        private static double GetFrameForCalculation(double frameRate)
        {
            if (Math.Abs(frameRate - 23.976) < 0.001)
            {
                return 24000.0 / 1001.0;
            }
            if (Math.Abs(frameRate - 29.97) < 0.001)
            {
                return 30000.0 / 1001.0;
            }
            if (Math.Abs(frameRate - 59.94) < 0.001)
            {
                return 60000.0 / 1001.0;
            }

            return frameRate;
        }

        private static int GetOptimalDisplayMilliseconds(string text, double optimalCharactersPerSecond, bool onlyOptimal = false)
        {
            if (optimalCharactersPerSecond < 2 || optimalCharactersPerSecond > 100)
            {
                optimalCharactersPerSecond = 14.7;
            }

            var duration = (double)CountCharactersAll(text) / optimalCharactersPerSecond * 1000.0;

            if (!onlyOptimal)
            {
                if (duration < 1400)
                {
                    duration *= 1.2;
                }
                else if (duration < 1400 * 1.2)
                {
                    duration = 1400 * 1.2;
                }
                else if (duration > 2900)
                {
                    duration = Math.Max(2900, duration * 0.96);
                }
            }

            if (duration < SubtitleMinimumDisplayMilliseconds)
            {
                duration = SubtitleMinimumDisplayMilliseconds;
            }

            if (duration > SubtitleMaximumDisplayMilliseconds)
            {
                duration = SubtitleMaximumDisplayMilliseconds;
            }

            return (int)Math.Round(duration, MidpointRounding.AwayFromZero);
        }

        private static decimal CountCharactersAll(string text)
        {
            var length = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (!char.IsControl(ch) &&
                    ch != '\u200B' &&
                    ch != '\uFEFF' &&
                    ch != '\u200E' &&
                    ch != '\u200F' &&
                    ch != '\u202A' &&
                    ch != '\u202B' &&
                    ch != '\u202C' &&
                    ch != '\u202D' &&
                    ch != '\u202E')
                {
                    length++;
                }
            }

            return length;
        }

    }

    class SubtitleConvert
    {
        public static bool IsSupportedByEmby(SubtitlesFormat format)
        {
            return format == SubtitlesFormat.SubRipFormat ||
                   format == SubtitlesFormat.SubStationAlphaFormat ||
                   format == SubtitlesFormat.WebVttFormat;
        }

        public static string GetExtSupportedByEmby(SubtitlesFormat format)
        {
            if (IsSupportedByEmby(format))
                return format.Extension;

            // file will be converted when downloaded
            return SubtitlesFormat.SubRipFormat.Extension;
        }

        public static string GetExtSupportedByEmby(string formatExt)
        {
            if (formatExt == null)
                return "srt";

            if (formatExt.EqualsIgnoreCase("ssa") ||
                formatExt.EqualsIgnoreCase("ass") ||
                formatExt.EqualsIgnoreCase("vtt"))
            {
                return formatExt.ToLower();
            }

            return "srt";
        }

        public static Stream ToSupportedFormat(
            Stream inStream, float fps, out string format, 
            SubEncodingCfg encoding, SubPostProcessingCfg postProcessing,
            Subtitle sub = null)
        {
            Stream outs = new MemoryStream();

            try
            {
                if (inStream == null)
                    throw new ArgumentNullException("inStream");

                if (!inStream.CanRead || !inStream.CanSeek)
                    throw new ArgumentException("Stream must be seekable and readable", "inStream");

                inStream.Seek(0, SeekOrigin.Begin);

                if (sub == null)
                    sub = Subtitle.Load(inStream, encoding, fps);

                if (sub == null)
                    throw new FormatException("Subtitle format not supported");

                if (postProcessing.AdjustDuration)
                {
                    sub.AdjustDuration(postProcessing.AdjustDurationCps, postProcessing.AdjustDurationExtendOnly);
                }

                if (!postProcessing.AdjustDuration && IsSupportedByEmby(sub.Format))
                {
                    // Do not convert formats supported by emby/jellyfin, just re-encode to UTF8 if needed
                    inStream.Seek(0, SeekOrigin.Begin);
                    var sr = new StreamReader(inStream, sub.Encoding, true);
                    var writer = new StreamWriter(outs, postProcessing.EncodeSubtitlesToUTF8 ? Encoding.UTF8 : sub.Encoding);
                    writer.Write(sr.ReadToEnd());
                    writer.Flush();
                    format = sub.Format.Extension;
                }
                else
                {
                    sub.WriteStream(outs, postProcessing.EncodeSubtitlesToUTF8, out format);
                }
                    
                outs.Seek(0, SeekOrigin.Begin);
                return outs;
            }
            catch 
            {
                outs.Close();
                throw;
            }
        }

    }
}
