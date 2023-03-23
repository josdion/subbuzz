using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace subbuzz.Helpers
{
    public class FileCacheItemNotFoundException : Exception
    {
        public FileCacheItemNotFoundException() { }
    }

    public class FileCacheItemExpiredException : Exception
    {
        public FileCacheItemExpiredException()  { }
    }

    public class FileCacheItemLockedException : Exception
    {
        public FileCacheItemLockedException(string message, Exception inner) : base(message, inner) { }
    }

    public class FileCacheItemOpenException : Exception
    {
        public FileCacheItemOpenException(string message, Exception inner) : base(message, inner) { }
    }

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
        protected TimeSpan Lifespan = TimeSpan.MaxValue;

        public FileCache(string cacheDir, TimeSpan? life = null)
        {
            CacheDir = cacheDir;
            Lifespan = life ?? TimeSpan.MaxValue;
        }

        public FileCache FromRegion(string[] region, TimeSpan? life = null)
        {
            if (region == null || region.Length < 1) return this;
            var paths = new List<string> { CacheDir };
            paths.AddRange(region);
            return new FileCache(Path.Combine(paths.ToArray()), life);
        }

        public FileCache FromRegion(string[] region, int lifeMinutes)
        {
            TimeSpan life = lifeMinutes < 1 ? TimeSpan.MaxValue : TimeSpan.FromMinutes(lifeMinutes);
            return FromRegion(region, life);
        }

        public void Add(string key, Stream value)
        {
            Add<NoData>(key, value, null);
        }

        public void Add<T>(string key, Stream value, T metaData)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            var (subDir, fileName) = GetFilePathName(key);

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
            var (subDir, fileName) = GetFilePathName(key);
            string fileNameMeta = Path.Combine(subDir, fileName + ExtMeta);
            string fileNameDat = Path.Combine(subDir, fileName + ExtDat);

            if (!File.Exists(fileNameMeta) || !File.Exists(fileNameDat))
            {
                throw new FileCacheItemNotFoundException();
            }

            using (FileStream streamMeta = GetStream(fileNameMeta, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var sr = new StreamReader(streamMeta, Encoding.UTF8))
                {
                    Meta<T> meta = JsonSerializer.Deserialize<Meta<T>>(sr.ReadToEnd());

                    if (DateTime.Now - meta.Timestamp > Lifespan)
                    {
                        throw new FileCacheItemExpiredException();
                    }

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

        protected (string, string) GetFilePathName(string key)
        {
            string keyHash = ComputeKeyHash(key);
            string[] pathParts = 
            { 
                CacheDir,
                keyHash.Substring(0, 3), 
                keyHash.Substring(3, 3), 
                keyHash.Substring(6, 3) 
            };
            return (Path.Combine(pathParts), keyHash.Substring(9));
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
                    if (ex.HResult != -2147024864 && // Windows ERROR_SHARING_VIOLATION 0x80070020
                        ex.HResult != 11) // EAGAIN Linux
                    {
                        throw new FileCacheItemOpenException($"File open error. HResult={ex.HResult}", ex);
                    }

                    if (totalTime >= 1000)
                    {
                        throw new FileCacheItemLockedException("File locked. HResult={ex.HResult}", ex);
                    }

                    Thread.Sleep(50);
                    totalTime += 50;
                }
            }
        }

    }
}
