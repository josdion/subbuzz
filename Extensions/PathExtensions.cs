using System;
using System.Collections.Generic;
using System.Text;

namespace subbuzz.Extensions
{
    public static class PathExtensions
    {
        public static string GetPathExtension(this string path)
        {
            var idx = path.LastIndexOf('.');
            if (idx == -1 || idx == path.Length - 1)
            {
                return string.Empty;
            }

            return path.Substring(idx+1);
        }

    }
}
