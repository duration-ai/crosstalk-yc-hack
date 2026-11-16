using System.Collections.Generic;
using Dogpatch.Look;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Nav
{
    // The direction collar (HUD v3): a chunky flat-shaded vertex-colour chevron lying on the road on
    // a ring of dashes around you, on foot or driving, that swings smoothly toward the next stretch
    // of the street path to the current objective (GroundCollar places it). Sunshine for routes,
    // waypoints and mission starts, hotline pink only for Operator asks. It pops in when an
    // objective appears and shrinks away on arrival.
    [DefaultExecutionOrder(10100)]
    public sealed class DirectionArrow : MonoBehaviour
    {
        const float Thickness = 0.32f;
        const float Chamfer = 0.1f;

        public NavService Nav;

        static readonly Vector2[] Outer =
        {
            new Vector2(0f, 1.05f), new Vector2(0.78f, 0.08f), new Vector2(0.3f, 0.08f), new Vector2(0.3f, -0.95f),
            new Vector2(-0.3f, -0.95f), new Vector2(-0.3f, 0.08f), new Vector2(-0.78f, 0.08f),
        };

        static readonly Vector2[] Inner =
        {
            new Vector2(0f, 0.88f), new Vector2(0.58f, 0.16f), new Vector2(0.2f, 0.16f), new Vector2(0.2f, -0.85f),
            new Vector2(-0.2f, -0.85f), new Vector2(-0.2f, 0.16f), new Vector2(-0.58f, 0.16f),
        };

        MeshFilter filter;
        MeshRenderer view;
        MeshRenderer ring;
        Mesh sunshine;
        Mesh pink;
        float shown;
        float yaw;
        float yawVelocity;
        bool placed;

        void Start()
        {
            sunshine = Build(SpeedDial.Hex("#FFCB1F"), "arrow-sunshine");
            pink = Build(SpeedDial.Hex("#FF3E8E"), "arrow-pink");
            var body = new GameObject("DirectionArrow");
            body.transform.SetParent(transform, false);
            filter = body.AddComponent<MeshFilter>();
            filter.sharedMesh = sunshine;
            view = body.AddComponent<MeshRenderer>();
            view.sharedMaterial = Nav.Game.SharedMaterial;
            view.shadowCastingMode = ShadowCastingMode.Off;
            view.receiveShadows = false;
            view.enabled = false;
            var dashes = new GameObject("CollarRing");
            dashes.transform.SetParent(transform, false);
            dashes.AddComponent<MeshFilter>().sharedMesh = HudV3.GroundCollar.Ring(SpeedDial.Hex("#FFCB1F"), SpeedDial.Hex("#1C2347"));
            ring = dashes.AddComponent<MeshRenderer>();
            ring.sharedMaterial = Nav.Game.SharedMaterial;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;
        }

        void LateUpdate()
        {
            var game = Nav.Game;
            var focus = game.Focus;
            var cam = game.View;
            bool want = Nav.HasObjective && !Nav.Arrived && focus != null && cam != null;
            shown = Mathf.MoveTowards(shown, want ? 1f : 0f, Time.deltaTime * (want ? 3.5f : 5f));
            view.enabled = shown > 0.001f;
            ring.enabled = view.enabled;
            if (!view.enabled)
            {
                placed = false;
                return;
            }
            if (focus == null || cam == null) return;

            filter.sharedMesh = Nav.Kind == NavKind.Ask ? pink : sunshine;
            var t = view.transform;
            float target = Mathf.Atan2(Nav.Heading.x, Nav.Heading.y) * Mathf.Rad2Deg;
            if (!placed) yaw = target;
            yaw = Mathf.SmoothDampAngle(yaw, target, ref yawVelocity, 0.12f);
            placed = true;
            float size = EaseOutBack(shown);
            HudV3.GroundCollar.Place(t, ring.transform, focus.position, yaw, size, game.CurrentCar != null);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        // Top face, a chamfer ring and straight sides, every face its own flat colour. Colours are
        // linear like the rest of the vertex-coloured world; alpha below 1 makes the top glow at night.
        static Mesh Build(Color top, string name)
        {
            var ink = SpeedDial.Hex("#1C2347");
            var rim = Color.Lerp(top, Color.white, 0.4f).linear;
            var side = Color.Lerp(top, ink, 0.22f).linear;
            var under = ink.linear;
            var face = top.linear;
            face.a = 0.5f;
            rim.a = 0.5f;
            var b = new Builder();
            float hi = Thickness, mid = Thickness - Chamfer;
            Vector3 P(Vector2 p, float y) => new Vector3(p.x, y, p.y);

            b.Tri(P(Inner[0], hi), P(Inner[1], hi), P(Inner[6], hi), Vector3.up, face);
            b.Quad(P(Inner[2], hi), P(Inner[3], hi), P(Inner[4], hi), P(Inner[5], hi), Vector3.up, face);
            b.Tri(P(Outer[0], 0f), P(Outer[1], 0f), P(Outer[6], 0f), Vector3.down, under);
            b.Quad(P(Outer[2], 0f), P(Outer[3], 0f), P(Outer[4], 0f), P(Outer[5], 0f), Vector3.down, under);
            for (int i = 0; i < Outer.Length; i++)
            {
                int j = (i + 1) % Outer.Length;
                var d = Outer[j] - Outer[i];
                var outward = new Vector3(-d.y, 0f, d.x).normalized;
                b.Quad(P(Outer[i], mid), P(Outer[j], mid), P(Inner[j], hi), P(Inner[i], hi), outward + Vector3.up, rim);
                b.Quad(P(Outer[i], 0f), P(Outer[j], 0f), P(Outer[j], mid), P(Outer[i], mid), outward, side);
            }
            return b.ToMesh(name);
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();

            // Winds the triangle so its front face looks along facing.
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color color)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
                foreach (var v in new[] { a, b, c })
                {
                    triangles.Add(vertices.Count);
                    vertices.Add(v);
                    colors.Add(color);
                }
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color color)
            {
                Tri(a, b, c, facing, color);
                Tri(a, c, d, facing, color);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
