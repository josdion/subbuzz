using System.Collections;

namespace subbuzz.Extensions
{
    public static class DictExtensions
    {
        public static bool IsNullOrEmpty(this IDictionary dict)
        {
            return dict == null || dict.Count < 1;
        }

        public static bool IsNotNullOrEmpty(this IDictionary dict)
        {
            return !dict.IsNullOrEmpty();
        }
    }
}
