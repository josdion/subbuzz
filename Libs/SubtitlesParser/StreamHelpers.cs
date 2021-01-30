using System.IO;

namespace SubtitlesParser.Classes
{
    static class StreamHelpers
    {
        /// <summary>
        /// Copies a stream to another stream.
        /// This method is useful in particular when the inputStream is not seekable.
        /// </summary>
        /// <param name="inputStream">The stream to copy</param>
        /// <returns>A copy of the input Stream</returns>
        public static Stream CopyStream(Stream inputStream)
        {
            var outputStream = new MemoryStream();
            int count;
            do
            {
                var buf = new byte[1024];
                count = inputStream.Read(buf, 0, 1024);
                outputStream.Write(buf, 0, count);
            } while (inputStream.CanRead && count > 0);
            outputStream.ToArray();

            return outputStream;
        }
    }
}
