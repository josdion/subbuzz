using subbuzz.Providers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace subbuzz.Helpers
{
    public class Utils
    {
        public static string Base64UrlEncode(string input)
        {
            byte[] encbuff = System.Text.Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToBase64String(encbuff).Replace("=", ",").Replace("+", "-").Replace("/", "_");
        }

        public static string Base64UrlDecode(string str)
        {
            byte[] decbuff = Convert.FromBase64String(str.Replace(",", "=").Replace("-", "+").Replace("_", "/"));
            return System.Text.Encoding.UTF8.GetString(decbuff);
        }

        public static string Base64UrlEncode<T>(T obj)
        {
            byte[] encbuff = JsonSerializer.SerializeToUtf8Bytes(obj, obj.GetType());
            return Convert.ToBase64String(encbuff).Replace("=", ",").Replace("+", "-").Replace("/", "_");
        }

        public static T Base64UrlDecode<T>(string id)
        {
            byte[] decbuff = Convert.FromBase64String(id.Replace(",", "=").Replace("-", "+").Replace("_", "/"));
            return JsonSerializer.Deserialize<T>(decbuff);
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);

            for (int i = 0; i < ba.Length; i++)
                hex.Append(ba[i].ToString("x2"));

            return hex.ToString();
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

        public static void MergeSubtitleInfo(List<SubtitleInfo> res, List<SubtitleInfo> sub)
        {
            foreach (var s in sub)
            {
                bool add = true;

                foreach (var r in res)
                {
                    if (s.Id == r.Id)
                    {
                        add = false;
                        break;
                    }
                }

                if (add)
                    res.Add(s);
            }
        }

    }
}
