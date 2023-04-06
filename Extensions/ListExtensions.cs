using System.Collections;

namespace subbuzz.Extensions
{
    public static class ListExtensions
    {
        public static bool IsNullOrEmpty(this IList list)
        {
            return list == null || list.Count < 1;
        }

        public static bool IsNotNullOrEmpty(this IList list)
        {
            return !list.IsNullOrEmpty();
        }
    }
}
