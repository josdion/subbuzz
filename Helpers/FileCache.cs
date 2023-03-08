using subbuzz.Extensions;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace subbuzz.Helpers
{
    public class FileCache
    {
        private const string ExtMeta = ".meta";
        private const string ExtDat = ".dat";

        protected class NoData { }

        public class Meta<T>
        {
            public DateTime Timestamp { get; set; }
            public string Key { get; set; }
            public T Data { get; set; }
        }

        public string CacheDir { get; protected set; }

        public FileCache(string cacheDir)
        {
            CacheDir = cacheDir;
        }

        public FileCache FromRegion(string region)
        {
            if (region.IsNullOrWhiteSpace()) return this;
            return new FileCache(Path.Combine(CacheDir, region));
        }

        public void Add(string key, Stream value)
        {
            Add<NoData>(key, value, null);
        }

        public void Add<T>(string key, Stream value, T metaData)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            string keyHash = ComputeKeyHash(key);
            string subDir = Path.Combine(CacheDir, keyHash.Substring(0, 3));
            string fileName = keyHash.Substring(3);

            if (!Directory.Exists(subDir))
                Directory.CreateDirectory(subDir);

            using (FileStream streamMeta = GetStream(Path.Combine(subDir, fileName + ExtMeta), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (FileStream streamDat = GetStream(Path.Combine(subDir, fileName + ExtDat), FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    value.Seek(0, SeekOrigin.Begin);
                    value.CopyTo(streamDat);
                    value.Seek(0, SeekOrigin.Begin);
                }

                Meta<T> meta = new Meta<T>
                {
                    Timestamp = DateTime.Now,
                    Key = key,
                    Data = metaData,
                };

                byte[] data = new UTF8Encoding(true).GetBytes(JsonSerializer.Serialize<Meta<T>>(meta));
                streamMeta.Write(data, 0, data.Length);
            }
        }

        public Stream Get(string key)
        {
            return Get<NoData>(key, out _);
        }

        public Stream Get<T>(string key, out T metaData)
        {
            string keyHash = ComputeKeyHash(key);
            string subDir = Path.Combine(CacheDir, keyHash.Substring(0, 3));
            string fileName = keyHash.Substring(3);
            string fileNameMeta = Path.Combine(subDir, fileName + ExtMeta);
            string fileNameDat = Path.Combine(subDir, fileName + ExtDat);

            if (!File.Exists(fileNameMeta) || !File.Exists(fileNameDat))
            {
                throw new FileNotFoundException();
            }

            using (FileStream streamMeta = GetStream(fileNameMeta, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var sr = new StreamReader(streamMeta, Encoding.UTF8))
                {
                    Meta<T> meta = JsonSerializer.Deserialize<Meta<T>>(sr.ReadToEnd());
                    metaData = meta.Data;

                    using (FileStream stream = GetStream(fileNameDat, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        Stream value = new MemoryStream();
                        stream.CopyTo(value);
                        value.Seek(0, SeekOrigin.Begin);
                        return value;
                    }
                }
            }
        }

        protected static string ComputeKeyHash(string key)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(key);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        protected FileStream GetStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            int totalTime = 0;
            while (true)
            {
                try
                {
                    return File.Open(path, mode, access, share);
                }
                catch (IOException ex)
                {
                    if ((ex.HResult & 0x0000FFFF) != 32 || totalTime > 500)
                    {
                         throw ex;
                    }

                    Thread.Sleep(50);
                    totalTime += 50;
                }
            }
        }


    }
}
