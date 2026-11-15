using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Something the Operator can be asked about: a building, tree, street light, billboard, car,
    // pedestrian or racer. Ids are stable across every client in a room (world pieces by build
    // order, racers' cars by player id) because effects name them as targets. Pieces of the merged
    // city meshes carry a MeshSpan; everything else is its own GameObject.
    public sealed class EntityTag : MonoBehaviour
    {
        public static readonly List<EntityTag> All = new List<EntityTag>();
        static readonly Dictionary<string, EntityTag> byId = new Dictionary<string, EntityTag>();

        public string Id;
        public string Class;
        public string Desc;
        public string Color;
        public bool Protected;
        public string ControlledBy = "";
        public MeshSpan Span;

        Renderer[] renderers;

        public static EntityTag Find(string id) => id != null && byId.TryGetValue(id, out var tag) && tag != null ? tag : null;

        public static EntityTag Add(GameObject go, string id, string cls, string desc, string color, bool isProtected = false, string controlledBy = "")
        {
            var tag = go.GetComponent<EntityTag>();
            if (tag == null) tag = go.AddComponent<EntityTag>();
            if (tag.Id != null) byId.Remove(tag.Id);
            tag.Id = id;
            tag.Class = cls;
            tag.Desc = desc;
            tag.Color = color;
            tag.Protected = isProtected;
            tag.ControlledBy = controlledBy ?? "";
            if (tag.isActiveAndEnabled) byId[id] = tag;
            return tag;
        }

        public Bounds WorldBounds
        {
            get
            {
                if (Span != null) return Span.Bounds;
                if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
                bool any = false;
                var bounds = new Bounds(transform.position, Vector3.one);
                foreach (var r in renderers)
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!any) bounds = r.bounds;
                    else bounds.Encapsulate(r.bounds);
                    any = true;
                }
                return bounds;
            }
        }

        public void ForgetRenderers() => renderers = null;

        void OnEnable()
        {
            All.Add(this);
            if (Id != null) byId[Id] = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Id != null && byId.TryGetValue(Id, out var tag) && tag == this) byId.Remove(Id);
        }
    }

    // The leaf classes of the effect catalogue and what they are a kind of:
    // vehicle > car > {waymo, zoox}; vehicle > {bus, bike}; person > {pedestrian, delivery_rider};
    // plant > tree; building, streetlight and billboard stand alone.
    public static class EntityClasses
    {
        static readonly Dictionary<string, string> Parent = new Dictionary<string, string>
        {
            { "waymo", "car" }, { "zoox", "car" }, { "car", "vehicle" }, { "bus", "vehicle" }, { "bike", "vehicle" },
            { "pedestrian", "person" }, { "delivery_rider", "person" }, { "tree", "plant" },
        };

        public static readonly string[] Leaves = { "waymo", "zoox", "car", "bus", "bike", "vehicle", "pedestrian", "delivery_rider", "person", "tree", "plant", "building", "streetlight", "billboard" };

        public static bool IsA(string cls, string kind)
        {
            for (var c = cls; c != null; c = Parent.TryGetValue(c, out var p) ? p : null)
                if (c == kind) return true;
            return false;
        }
    }

    // Plain colour words for the snapshot ("white", "red") from a linear or gamma colour.
    public static class ColorWords
    {
        static readonly (string name, Color color)[] Palette =
        {
            ("white", new Color(0.95f, 0.95f, 0.93f)), ("black", new Color(0.08f, 0.08f, 0.1f)), ("grey", new Color(0.5f, 0.5f, 0.52f)),
            ("silver", new Color(0.75f, 0.76f, 0.78f)), ("red", new Color(0.85f, 0.15f, 0.15f)), ("orange", new Color(0.95f, 0.55f, 0.15f)),
            ("yellow", new Color(0.95f, 0.85f, 0.2f)), ("green", new Color(0.25f, 0.65f, 0.3f)), ("blue", new Color(0.2f, 0.45f, 0.9f)),
            ("purple", new Color(0.6f, 0.35f, 0.9f)), ("pink", new Color(0.98f, 0.5f, 0.7f)), ("brown", new Color(0.55f, 0.38f, 0.25f)),
            ("beige", new Color(0.88f, 0.8f, 0.65f)), ("gold", new Color(0.85f, 0.68f, 0.25f)),
        };

        public static string Nearest(Color gamma)
        {
            string best = "grey";
            float bestD = float.MaxValue;
            foreach (var (name, c) in Palette)
            {
                float d = (gamma.r - c.r) * (gamma.r - c.r) * 0.3f + (gamma.g - c.g) * (gamma.g - c.g) * 0.59f + (gamma.b - c.b) * (gamma.b - c.b) * 0.11f;
                Color.RGBToHSV(gamma, out float h1, out float s1, out _);
                Color.RGBToHSV(c, out float h2, out float s2, out _);
                float dh = Mathf.Abs(h1 - h2);
                dh = Mathf.Min(dh, 1f - dh);
                d += dh * dh * Mathf.Min(s1, s2) * 0.6f;
                if (d < bestD)
                {
                    bestD = d;
                    best = name;
                }
            }
            return best;
        }

        public static string NearestLinear(Color linear) => Nearest(linear.gamma);
    }
}
