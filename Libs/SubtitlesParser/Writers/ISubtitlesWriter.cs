using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SubtitlesParser.Classes.Writers
{
    /// <summary>
    /// Interface specifying the required method for a SubWriter
    /// </summary>
    public interface ISubtitlesWriter
    {
        /// <summary>
        /// Writes a list of SubtitleItems into a stream 
        /// </summary>
        /// <param name="stream">the stream to write to</param>
        /// <param name="subtitleItems">the SubtitleItems to write</param>
        /// <param name="includeFormatting">if formatting codes should be included when writing the subtitle item lines. Each subtitle item must have the PlaintextLines property set.</param>
        void WriteStream(Stream stream, Encoding encoding, IEnumerable<SubtitleItem> subtitleItems, bool includeFormatting = true);

        /// <summary>
        /// Asynchronously writes a list of SubtitleItems into a stream 
        /// </summary>
        /// <param name="stream">the stream to write to</param>
        /// <param name="subtitleItems">the SubtitleItems to write</param>
        /// <param name="includeFormatting">if formatting codes should be included when writing the subtitle item lines. Each subtitle item must have the PlaintextLines property set.</param>
        Task WriteStreamAsync(Stream stream, Encoding encoding, IEnumerable<SubtitleItem> subtitleItems, bool includeFormatting = true);
    }
}
