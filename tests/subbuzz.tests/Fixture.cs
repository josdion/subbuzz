using System;
using System.IO;

namespace subbuzz.tests
{
    internal static class Fixture
    {
        public static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        }
    }
}
