using System;

namespace subbuzz.Helpers
{
    public class Utils
    {
        public static string Base64UrlDecode(string str)
        {
            byte[] decbuff = Convert.FromBase64String(str.Replace(",", "=").Replace("-", "+").Replace("/", "_"));
            return System.Text.Encoding.UTF8.GetString(decbuff);
        }

        public static string Base64UrlEncode(string input)
        {
            byte[] encbuff = System.Text.Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToBase64String(encbuff).Replace("=", ",").Replace("+", "-").Replace("_", "/");
        }

        public static string TrimStringStart(string str, string remove, StringComparison comparisonType = StringComparison.CurrentCultureIgnoreCase)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(remove))
            {
                return str;
            }

            while (true)
            {
                str = str.TrimStart();
                if (!str.StartsWith(remove, comparisonType)) break;
                str = str.Substring(remove.Length);
            }

            return str;
        }

        public static string TrimStringEnd(string str, string remove, StringComparison comparisonType = StringComparison.CurrentCultureIgnoreCase)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(remove))
            {
                return str;
            }

            while (true)
            {
                str = str.TrimEnd();
                if (!str.EndsWith(remove, comparisonType)) break;
                str = str.Substring(0, str.Length - remove.Length);
            }

            return str;
        }

        public static string TrimString(string str, string remove, StringComparison comparisonType = StringComparison.CurrentCultureIgnoreCase)
        {
            return TrimStringStart(TrimStringEnd(str, remove, comparisonType), remove, comparisonType);
        }

    }
}
