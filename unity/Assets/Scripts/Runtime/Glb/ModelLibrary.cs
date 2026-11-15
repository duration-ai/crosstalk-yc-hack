using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.Glb
{
    // Loads Polyfork GLBs committed under Resources/Polyfork and caches the parsed assets.
    public sealed class ModelLibrary
    {
        readonly Dictionary<string, GlbAsset> cache = new Dictionary<string, GlbAsset>();
        readonly Dictionary<string, List<ModelEntry>> byRole = new Dictionary<string, List<ModelEntry>>();

        public ModelLibrary()
        {
            var catalog = JsonUtility.FromJson<ModelCatalog>(Resources.Load<TextAsset>("Polyfork/catalog").text);
            foreach (var entry in catalog.models)
            {
                if (!byRole.TryGetValue(entry.role, out var list)) byRole[entry.role] = list = new List<ModelEntry>();
                list.Add(entry);
            }
        }

        public IReadOnlyList<ModelEntry> Role(string role) =>
            byRole.TryGetValue(role, out var list) ? list : (IReadOnlyList<ModelEntry>)System.Array.Empty<ModelEntry>();

        public GlbAsset Get(string id)
        {
            if (cache.TryGetValue(id, out var asset)) return asset;
            var bytes = Resources.Load<TextAsset>("Polyfork/" + id);
            if (bytes == null)
            {
                Debug.LogWarning("[Dogpatch] missing model " + id);
                return null;
            }
            asset = GlbAsset.Load(id, bytes.bytes);
            Look.PolyforkPalette.Apply(asset);
            cache[id] = asset;
            return asset;
        }
    }
}
