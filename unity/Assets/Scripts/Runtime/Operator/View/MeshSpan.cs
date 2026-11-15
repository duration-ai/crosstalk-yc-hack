using System;
using System.Collections.Generic;
using Dogpatch.World;
using UnityEngine;

namespace Dogpatch.Operator
{
    // A tagged thing made of static instances inside the merged city chunks (a building's fitted
    // models, a tree, a lamp, a billboard). Recolouring writes the instances' vertex colours in the
    // chunk; hiding and lifting out go through StaticInstanceIndex.Detach/Restore, so the city's
    // own breakage bookkeeping stays right.
    public sealed class MeshSpan
    {
        public StaticInstanceIndex Index;
        public List<StaticInstance> Parts = new List<StaticInstance>();
        public Bounds Bounds;

        readonly Dictionary<int, Color[]> original = new Dictionary<int, Color[]>();

        public bool Hidden
        {
            get
            {
                foreach (var p in Parts) if (Index.IsDetached(p.Id)) return true;
                return false;
            }
        }

        public void Add(StaticInstance part)
        {
            if (Parts.Count == 0) Bounds = part.WorldBounds;
            else Bounds.Encapsulate(part.WorldBounds);
            Parts.Add(part);
        }

        // paint(original colour, world position, brightest original in the span) per vertex.
        public void Paint(Func<Color, Vector3, float, Color> paint)
        {
            float brightest = 1e-4f;
            foreach (var part in Parts)
                foreach (var c in Original(part)) brightest = Mathf.Max(brightest, 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
            foreach (var group in ByChunk())
            {
                var mesh = group.Key.sharedMesh;
                var colors = mesh.colors;
                var vertices = mesh.vertices;
                foreach (var part in group.Value)
                {
                    var from = Original(part);
                    for (int i = 0; i < part.VertexCount; i++) colors[part.VertexStart + i] = paint(from[i], vertices[part.VertexStart + i], brightest);
                }
                mesh.colors = colors;
            }
        }

        Color[] Original(StaticInstance part)
        {
            if (original.TryGetValue(part.Id, out var colors)) return colors;
            colors = new Color[part.VertexCount];
            var all = part.Chunk.sharedMesh.colors;
            if (all.Length > 0) Array.Copy(all, part.VertexStart, colors, 0, part.VertexCount);
            return original[part.Id] = colors;
        }

        Dictionary<MeshFilter, List<StaticInstance>> ByChunk()
        {
            var groups = new Dictionary<MeshFilter, List<StaticInstance>>();
            foreach (var part in Parts)
            {
                if (part.Chunk == null) continue;
                if (!groups.TryGetValue(part.Chunk, out var list)) groups[part.Chunk] = list = new List<StaticInstance>();
                list.Add(part);
            }
            return groups;
        }

        public void Show()
        {
            foreach (var p in Parts) Index.Restore(p.Id);
        }

        // A standalone copy of every part under one pivot at the bottom centre of the bounds, with
        // the parts hidden in their chunks.
        public GameObject Detach(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(Bounds.center.x, Bounds.min.y, Bounds.center.z);
            foreach (var part in Parts)
            {
                var mesh = Index.Detach(part.Id);
                if (mesh == null) continue;
                var child = new GameObject(part.Variant);
                var m = part.Transform;
                child.transform.SetPositionAndRotation(m.GetPosition(), m.rotation);
                child.transform.localScale = m.lossyScale;
                child.transform.SetParent(go.transform, true);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            return go;
        }
    }
}
