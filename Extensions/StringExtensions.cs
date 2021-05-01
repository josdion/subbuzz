using System;
using System.Collections.Generic;
using System.Linq;

namespace subbuzz.Extensions
{
    public static class StringExtensions
    {
        public static bool ContainsIgnoreCase(this string text, string contains)
        {
            return text.IndexOf(contains, StringComparison.InvariantCultureIgnoreCase) > -1;
        }

        public static bool ContainsIgnoreCase(this IEnumerable<string> source, string value)
        {
            return source.Contains(value, StringComparer.InvariantCultureIgnoreCase);
        }
    }
}
