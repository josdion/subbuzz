using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace subbuzz.Extensions
{
    public static class StringExtensions
    {

        public static string RemoveAccent(this string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static bool IsNullOrWhiteSpace(this string text)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        public static bool IsNotNullOrWhiteSpace(this string text)
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        public static bool EqualsIgnoreCase(this string text, string equals)
        {
            return text.Equals(equals, StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool ContainsIgnoreCase(this string text, string contains)
        {
            return text.IndexOf(contains, StringComparison.InvariantCultureIgnoreCase) > -1;
        }

        public static bool Contains(this string text, string contains, StringComparison comparisonType = StringComparison.Ordinal)
        {
            return text.IndexOf(contains, comparisonType) >= 0;
        }

        public static bool ContainsIgnoreCase(this IEnumerable<string> source, string value)
        {
            return source.Contains(value, StringComparer.InvariantCultureIgnoreCase);
        }
    }
}
