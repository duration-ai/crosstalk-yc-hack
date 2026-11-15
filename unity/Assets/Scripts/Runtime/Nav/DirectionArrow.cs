using System.Collections.Generic;
using Dogpatch.Look;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dogpatch.Nav
{
    // The Hit & Run arrow: a chunky flat-shaded vertex-colour arrow floating near the top of the
    // view, ahead of you and at your depth, on foot or driving, that swings smoothly toward the next
    // stretch of the street path to the current objective. It runs after the follow camera and
    // the camera feel (shake, speed FOV), so it stays pinned to the same spot on screen. It leans back toward the camera so its top face reads as an arrow from the
    // low chase view. Sunshine for routes and waypoints, hotline pink when it leads to a mission
    // start (the race's start ring). It pops in when an objective appears and shrinks away on arrival.
    [DefaultExecutionOrder(10100)]
    public sealed class DirectionArrow : MonoBehaviour
    {
        const float Thickness = 0.32f;
        const float Chamfer = 0.1f;
        const float Tilt = 32f;

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
        }

        void LateUpdate()
        {
            var game = Nav.Game;
            var focus = game.Focus;
            var cam = game.View;
            bool want = Nav.HasObjective && !Nav.Arrived && focus != null && cam != null;
            shown = Mathf.MoveTowards(shown, want ? 1f : 0f, Time.deltaTime * (want ? 3.5f : 5f));
            view.enabled = shown > 0.001f;
            if (!view.enabled)
            {
                placed = false;
                return;
            }
            if (focus == null || cam == null) return;

            filter.sharedMesh = Nav.Kind == NavKind.Mission ? pink : sunshine;
            float scale = game.CurrentCar != null ? 1f : 0.65f;
            var t = view.transform;
            float target = Mathf.Atan2(Nav.Heading.x, Nav.Heading.y) * Mathf.Rad2Deg;
            if (!placed) yaw = target;
            yaw = Mathf.SmoothDampAngle(yaw, target, ref yawVelocity, 0.14f);
            placed = true;
            t.rotation = Quaternion.AngleAxis(-Tilt, cam.transform.right) * Quaternion.Euler(0f, yaw, 0f);
            float size = scale * EaseOutBack(shown);
            t.localScale = new Vector3(size, size, size);
            PlaceUnderTopRow(t, cam, Mathf.Max(Vector3.Distance(cam.transform.position, focus.position), 4f));
            t.position += Vector3.up * Mathf.Sin(Time.time * 3.2f) * 0.05f;
        }

        // Puts the arrow depth metres along the camera ray through the top middle of the safe area,
        // as high as it goes with its top edge just under the top HUD row (place, meters, pause, and
        // in portrait the news band that drops below them). Read from Screen and the safe area every
        // frame, so it follows rotation; measured from the arrow's projected corners, so any yaw fits.
        void PlaceUnderTopRow(Transform t, Camera cam, float depth)
        {
            var safe = Screen.safeArea;
            float s = Mathf.Min(Screen.height / 402f, Screen.width / 560f);
            float limit = safe.yMax - (safe.width - 44f * s < 700f * s ? 116f : 88f) * s;
            Vector3 At(float y) => cam.ScreenPointToRay(new Vector3(safe.center.x, y, 0f)).GetPoint(depth);
            t.position = At(limit);
            var b = filter.sharedMesh.bounds;
            float top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                top = Mathf.Max(top, cam.WorldToScreenPoint(t.TransformPoint(corner)).y);
            }
            t.position = At(limit - (top - limit) - 6f * s);
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
