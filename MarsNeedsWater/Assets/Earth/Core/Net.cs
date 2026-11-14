using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace MarsNeedsWater.Earth
{
    /// <summary>Blocking HTTP helpers, meant to be called from worker threads only.</summary>
    public static class Net
    {
        static readonly HttpClient Client = Make();
        public static long BytesDownloaded;
        public static int Requests;
        public static volatile string LastError;

        static HttpClient Make()
        {
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("mini-earth-game/1.0");
            return c;
        }

        public static byte[] Get(string url, long offset = -1, long length = 0)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        if (offset >= 0) req.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
                        using (var resp = Client.SendAsync(req).GetAwaiter().GetResult())
                        {
                            int code = (int)resp.StatusCode;
                            if (code == 404 || code == 403) return null;
                            resp.EnsureSuccessStatusCode();
                            var data = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                            Interlocked.Add(ref BytesDownloaded, data.Length);
                            Interlocked.Increment(ref Requests);
                            return data;
                        }
                    }
                }
                catch (Exception e)
                {
                    last = e;
                    Thread.Sleep(400 * (attempt + 1));
                }
            }
            LastError = last != null ? last.GetBaseException().Message : "download failed";
            throw new IOException("Download failed: " + url, last);
        }

        public static byte[] Gunzip(byte[] data)
        {
            if (data == null || data.Length < 2 || data[0] != 0x1f || data[1] != 0x8b) return data;
            using (var src = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var dst = new MemoryStream(data.Length * 4))
            {
                src.CopyTo(dst);
                return dst.ToArray();
            }
        }

        /// <summary>Write through a temp file so a crash never leaves half a file in the cache.</summary>
        public static void WriteFile(string path, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + "." + Thread.CurrentThread.ManagedThreadId + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception) { }
        }
    }

    /// <summary>Small thread-safe cache that loads each key once, even when several threads ask together.</summary>
    public sealed class Once<TKey, TValue> where TValue : class
    {
        readonly Dictionary<TKey, Lazy<TValue>> map = new Dictionary<TKey, Lazy<TValue>>();
        readonly Queue<TKey> order = new Queue<TKey>();
        readonly int capacity;

        public Once(int capacity) { this.capacity = capacity; }

        public TValue Get(TKey key, Func<TValue> load)
        {
            Lazy<TValue> lazy;
            lock (map)
            {
                if (!map.TryGetValue(key, out lazy))
                {
                    lazy = new Lazy<TValue>(load, LazyThreadSafetyMode.ExecutionAndPublication);
                    map[key] = lazy;
                    order.Enqueue(key);
                    while (order.Count > capacity) map.Remove(order.Dequeue());
                }
            }
            try { return lazy.Value; }
            catch (Exception)
            {
                lock (map) { Lazy<TValue> cur; if (map.TryGetValue(key, out cur) && cur == lazy) map.Remove(key); }
                throw;
            }
        }
    }
}
