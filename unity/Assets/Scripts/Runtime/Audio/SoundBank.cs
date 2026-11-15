using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Dogpatch.Audio
{
    // Every clip under Resources/Audio, loaded once. "crunch_heavy" resolves to a random variant
    // among crunch_heavy_1..n (never the same twice in a row); an exact name wins when it exists.
    public static class SoundBank
    {
        static readonly string[] Folders = { "Engines", "Vehicles", "Impacts", "Breakables", "Ambience", "People", "Ui" };
        static readonly Dictionary<string, AudioClip> exact = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();
        static readonly Dictionary<string, int> last = new Dictionary<string, int>();
        static bool loaded;

        public static int Count => exact.Count;
        public static long Bytes { get; private set; }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            foreach (var folder in Folders)
            {
                foreach (var clip in Resources.LoadAll<AudioClip>("Audio/" + folder)) Add(clip);
            }
            Add(Resources.Load<AudioClip>("Audio/Music/results_sting"));
        }

        public static void Add(AudioClip clip)
        {
            if (clip == null || exact.ContainsKey(clip.name)) return;
            exact[clip.name] = clip;
            Bytes += Profiler.GetRuntimeMemorySizeLong(clip);
            int us = clip.name.LastIndexOf('_');
            if (us > 0 && int.TryParse(clip.name.Substring(us + 1), out _))
            {
                string key = clip.name.Substring(0, us);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
        }

        public static AudioClip Get(string name)
        {
            Load();
            if (string.IsNullOrEmpty(name)) return null;
            if (exact.TryGetValue(name, out var clip)) return clip;
            if (!groups.TryGetValue(name, out var list) || list.Count == 0) return null;
            last.TryGetValue(name, out int prev);
            int pick = list.Count == 1 ? 0 : Random.Range(0, list.Count - 1);
            if (list.Count > 1 && pick >= prev) pick++;
            last[name] = pick;
            return list[pick];
        }

        public static bool Has(string name) => Get(name) != null;
    }
}
