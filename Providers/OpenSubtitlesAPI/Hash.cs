using subbuzz.Helpers;
using System;
using System.IO;

namespace subbuzz.Providers.OpenSubtitlesAPI
{
    public class Hash
    {
        public static string ComputeHash(Stream stream)
        {
            var hash = ComputeMovieHash(stream);
            return Utils.ByteArrayToString(hash);
        }

        private static byte[] ComputeMovieHash(Stream input)
        {
            using (input)
            {
                long streamSize = input.Length, lHash = streamSize;
                int size = sizeof(long), count = 65536 / size;
                var buffer = new byte[size];

                for (int i = 0; i < count && input.Read(buffer, 0, size) > 0; i++)
                {
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                input.Position = Math.Max(0, streamSize - 65536);

                for (int i = 0; i < count && input.Read(buffer, 0, size) > 0; i++)
                {
                    lHash += BitConverter.ToInt64(buffer, 0);
                }

                var result = BitConverter.GetBytes(lHash);
                Array.Reverse(result);

                return result;
            }
        }

    }
}
