using System.Collections.Generic;
using Dogpatch.Data;
using UnityEngine;

namespace Dogpatch.Glb
{
    // Loads Polyfork GLBs committed under Resources/Polyfork and caches the parsed assets.
    // catalog.json lists the stock models (player, cars, NPCs); variants.json lists the world's
    // recoloured, knob-built variants (buildings, trees, props) written by tools/PolyforkBake.
    public sealed class ModelLibrary
    {
        readonly Dictionary<string, GlbAsset> cache = new Dictionary<string, GlbAsset>();
        readonly Dictionary<string, List<ModelEntry>> byRole = new Dictionary<string, List<ModelEntry>>();
        readonly Dictionary<string, ModelEntry> byId = new Dictionary<string, ModelEntry>();

        public ModelLibrary()
        {
            foreach (var file in new[] { "Polyfork/catalog", "Polyfork/variants" })
            {
                var text = Resources.Load<TextAsset>(file);
                if (text == null) continue;
                foreach (var entry in JsonUtility.FromJson<ModelCatalog>(text.text).models)
                {
                    if (!byRole.TryGetValue(entry.role, out var list)) byRole[entry.role] = list = new List<ModelEntry>();
                    list.Add(entry);
                    byId[entry.id] = entry;
                }
            }
        }

        public IReadOnlyList<ModelEntry> Role(string role) =>
            byRole.TryGetValue(role, out var list) ? list : (IReadOnlyList<ModelEntry>)System.Array.Empty<ModelEntry>();

        public ModelEntry Entry(string id) => byId.TryGetValue(id, out var e) ? e : null;

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
            cache[id] = asset;
            return asset;
        }
    }
}
