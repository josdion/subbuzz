using subbuzz.Helpers;
using SubtitlesParser.Classes;
using System;
using System.Collections.Generic;
using System.IO;

namespace subbuzz.Providers.Http
{
    public class File : IDisposable
    {
        public string Name { get; set; }
        public string Ext { get; set; }
        public Stream Content { get; set; }
        public Subtitle? Sub { get; set; } = null;
        public void Dispose() => Content?.Dispose();

        public bool IsSubfile()
        {
            return Sub == null ? false : SubtitlesFormat.IsFormatSupported(Sub.Format);
        }

        public string GetExtSupportedByEmby()
        {
            if (Sub == null) return null;
            return SubtitleConvert.GetExtSupportedByEmby(Sub.Format);
        }

    };

    public class FileList : List<File>, IDisposable
    {
        public void Dispose()
        {
            foreach (var item in this)
            {
                item.Dispose();
            }
        }

        public int SubCount
        {
            get {
                int count = 0;
                foreach (var item in this)
                {
                    if (item.IsSubfile()) count++;
                }
                return count;
            }
        }

    }

}
