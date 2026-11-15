using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dogpatch.Backend;
using UnityEngine;
using UnityEngine.Networking;
using Avatar = Dogpatch.Backend.Avatar;

namespace Dogpatch.Glb
{
    // Turns a backend avatar into a GlbAsset the player can wear. Every Polyfork humanoid shares
    // one 31-bone skeleton with the same bone names and identity rest rotations, so:
    //   - the remix GLB (geometry knobs baked in, no clips, no normals) gets the bundled player's
    //     Idle/Walk/Run retargeted onto it by bone name;
    //   - the recolour swaps each colour-zone default ("from", sRGB hex) for the player's "to";
    //   - if the remix can't be fetched (any non-200, e.g. Polyfork's per-IP bake budget), the
    //     stock GLB is used instead, which has its own clips.
    // Build, figure, age and expression are baked into the remix, so the model is never rescaled.
    // Downloads are cached under persistentDataPath/avatars keyed by a hash of the URL; a stock
    // Polyfork URL whose model ships in Resources/Polyfork is read from there instead.
    public static class AvatarLoader
    {
        public static string CacheDirectory => Path.Combine(Application.persistentDataPath, "avatars");

        public static async Task<GlbAsset> LoadAsync(Avatar avatar, GlbAsset clipSource)
        {
            var bytes = await FetchAsync(avatar.glbUrl);
            if (bytes == null) bytes = await FetchAsync(avatar.fallbackGlbUrl);
            if (bytes == null) return null;
            return Prepare(GlbAsset.Load("avatar-" + avatar.@base, bytes), avatar, clipSource);
        }

        public static GlbAsset Prepare(GlbAsset glb, Avatar avatar, GlbAsset clipSource)
        {
            Recolor(glb, avatar.recolor);
            if (glb.Clips.Length == 0 && clipSource != null) glb.Clips = Retarget(clipSource, glb);
            return glb;
        }

        static async Task<byte[]> FetchAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var bundled = Bundled(url);
            if (bundled != null) return bundled;
            string path = Path.Combine(CacheDirectory, Hash(url) + ".glb");
            if (File.Exists(path)) return File.ReadAllBytes(path);

            using var request = UnityWebRequest.Get(url);
            request.timeout = 60;
            await AvatarHttpClient.Send(request);
            var data = request.downloadHandler.data;
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200 || !IsGlb(data))
            {
                Debug.LogWarning($"[Avatar] fetch {request.responseCode} {request.error} {url}");
                return null;
            }
            Debug.Log($"[Avatar] fetched {data.Length / 1024} KB x-remix={request.GetResponseHeader("x-remix") ?? "-"} {url}");
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllBytes(path, data);
            return data;
        }

        // https://polyfork.dev/cdn/<id>.glb with no query is the stock model; the commuter (the
        // stub's answer) ships byte-identical in Resources/Polyfork.
        public static byte[] Bundled(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "polyfork.dev" || uri.Query.Length > 0) return null;
            if (!uri.AbsolutePath.StartsWith("/cdn/") || !uri.AbsolutePath.EndsWith(".glb")) return null;
            var asset = Resources.Load<TextAsset>("Polyfork/" + Path.GetFileNameWithoutExtension(uri.AbsolutePath));
            return asset != null && IsGlb(asset.bytes) ? asset.bytes : null;
        }

        public static bool IsGlb(byte[] data) => data != null && data.Length > 20 && BitConverter.ToUInt32(data, 0) == 0x46546C67;

        public static string Hash(string url)
        {
            using var sha = SHA1.Create();
            var sb = new StringBuilder();
            foreach (var b in sha.ComputeHash(Encoding.UTF8.GetBytes(url))) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // GlbAsset keeps COLOR_0 as linear floats; zone defaults are sRGB hex. Each vertex is
        // compared in sRGB 8-bit, allowing one step of float rounding (neighbouring zones on the
        // same model sit at least five steps apart). Returns the number of vertices changed.
        public static int Recolor(GlbAsset glb, AvatarRecolor[] entries)
        {
            if (entries == null || entries.Length == 0) return 0;
            var from = new List<Color32>();
            var to = new List<Color>();
            foreach (var e in entries)
            {
                if (!ColorUtility.TryParseHtmlString(e.from, out var f) || !ColorUtility.TryParseHtmlString(e.to, out var t)) continue;
                from.Add(f);
                to.Add(t.linear);
            }

            int changed = 0;
            foreach (var mesh in glb.Meshes)
            {
                var colors = mesh.colors;
                bool dirty = false;
                for (int v = 0; v < colors.Length; v++)
                {
                    var c = colors[v];
                    int r = ToSrgb(c.r), g = ToSrgb(c.g), b = ToSrgb(c.b);
                    for (int k = 0; k < from.Count; k++)
                    {
                        if (Mathf.Abs(r - from[k].r) > 1 || Mathf.Abs(g - from[k].g) > 1 || Mathf.Abs(b - from[k].b) > 1) continue;
                        colors[v] = new Color(to[k].r, to[k].g, to[k].b, c.a);
                        dirty = true;
                        changed++;
                        break;
                    }
                }
                if (dirty) mesh.colors = colors;
            }
            return changed;
        }

        static int ToSrgb(float linear) => Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(linear)) * 255f);

        // Rotations copy across as they are (rest rotations are identity on every Polyfork
        // humanoid). Translation keys are absolute, so they scale by the ratio of the two bones'
        // rest offsets: a shorter or taller figure keeps its feet on the ground.
        public static GlbClip[] Retarget(GlbAsset source, GlbAsset target)
        {
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < target.Nodes.Length; i++)
                if (!byName.ContainsKey(target.Nodes[i].Name)) byName[target.Nodes[i].Name] = i;

            var clips = new List<GlbClip>();
            foreach (var clip in source.Clips)
            {
                var channels = new List<GlbChannel>();
                foreach (var channel in clip.Channels)
                {
                    var bone = source.Nodes[channel.Node];
                    if (!byName.TryGetValue(bone.Name, out int node)) continue;
                    var values = channel.Values;
                    if (!channel.IsRotation)
                    {
                        float rest = bone.Translation.magnitude;
                        float scale = rest > 1e-4f ? target.Nodes[node].Translation.magnitude / rest : 1f;
                        values = new Vector4[channel.Values.Length];
                        for (int k = 0; k < values.Length; k++) values[k] = channel.Values[k] * scale;
                    }
                    channels.Add(new GlbChannel { Node = node, IsRotation = channel.IsRotation, Times = channel.Times, Values = values });
                }
                clips.Add(new GlbClip { Name = clip.Name, Length = clip.Length, Channels = channels.ToArray() });
            }
            return clips.ToArray();
        }
    }
}
