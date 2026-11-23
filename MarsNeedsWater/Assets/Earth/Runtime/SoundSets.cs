using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Recordings from a Resources folder, grouped into sets by name: "skid_road_1", "skid_road_2"... are the set
    /// "skid_road". The trailing number is kept too (the engine loops use it for their pitch). A set never hands out
    /// the same clip twice running.
    /// </summary>
    public sealed class SoundSets
    {
        public struct Clip { public AudioClip Audio; public int Number; }

        readonly Dictionary<string, Clip[]> sets = new Dictionary<string, Clip[]>();
        readonly Dictionary<string, int> last = new Dictionary<string, int>();
        readonly System.Random rnd;

        public SoundSets(string folder, int seed)
        {
            rnd = new System.Random(seed);
            var found = new Dictionary<string, List<Clip>>();
            foreach (AudioClip c in Resources.LoadAll<AudioClip>(folder))
            {
                string key = c.name; int number = 0;
                int u = key.LastIndexOf('_');
                if (u > 0 && int.TryParse(key.Substring(u + 1), out number)) key = key.Substring(0, u); else number = 0;
                if (!found.TryGetValue(key, out var list)) found[key] = list = new List<Clip>();
                list.Add(new Clip { Audio = c, Number = number });
            }
            foreach (var kv in found)
            {
                kv.Value.Sort((a, b) => a.Number.CompareTo(b.Number));
                sets[kv.Key] = kv.Value.ToArray();
            }
        }

        public bool Has(string key) { return sets.ContainsKey(key); }
        public Clip[] All(string key) { return sets.TryGetValue(key, out var a) ? a : null; }
        public float Random01() { return (float)rnd.NextDouble(); }

        /// <summary>The first of these names that has recordings, or null.</summary>
        public string First(params string[] keys)
        {
            foreach (string k in keys) if (sets.ContainsKey(k)) return k;
            return null;
        }

        /// <summary>One clip of a set, not the one picked last time. Null if the set is missing.</summary>
        public AudioClip Pick(string key)
        {
            if (key == null || !sets.TryGetValue(key, out var set) || set.Length == 0) return null;
            int i = rnd.Next(set.Length);
            if (set.Length > 1 && last.TryGetValue(key, out int was) && i == was) i = (i + 1 + rnd.Next(set.Length - 1)) % set.Length;
            last[key] = i;
            return set[i].Audio;
        }

        /// <summary>A silent looping source on its own object (so it can carry its own filter), already playing.</summary>
        public AudioSource Loop(Transform parent, string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = 0f; s.playOnAwake = false; s.dopplerLevel = 0f;
            if (clip != null) { s.time = Random01() * clip.length * 0.9f; s.Play(); }
            return s;
        }
    }
}
