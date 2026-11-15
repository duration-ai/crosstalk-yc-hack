using System.Collections.Generic;
using Dogpatch.World;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // The race's own objects in the world, all flat vertex-colored geometry on the shared material:
    // the start ring (its beacon and floor overlay are StartRingFx), the YC finish (checkered line, banner and a tall checkered flag), turn
    // and chevrons painted on the route, and the pink ask tokens.
    public static class CourseDressing
    {
        static readonly Color Yellow = PaintJob.Linear("#FFCB1F");
        static readonly Color White = PaintJob.Linear("#F7F8F9");
        static readonly Color Ink = PaintJob.Linear("#1C2347");
        static readonly Color Pink = PaintJob.Linear("#FF3E8E");
        static readonly Color DeepPink = PaintJob.Linear("#C8246A");

        static GameObject Emit(Transform parent, string name, MeshBuilder mb, Vector3 position, float heading, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, heading, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Vector3 Polar(float angle, float radius, float y) => new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

        static void Annulus(MeshBuilder mb, float inner, float outer, float y, Color color, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                if (inner <= 0f) mb.Triangle(new Vector3(0f, y, 0f), Polar(a0, outer, y), Polar(a1, outer, y), color, Vector3.up);
                else mb.Quad(Polar(a0, inner, y), Polar(a0, outer, y), Polar(a1, outer, y), Polar(a1, inner, y), color, Vector3.up);
            }
        }

        static void Chevron(MeshBuilder mb, Vector3 center, Vector3 along, Vector3 across, float length, float halfWidth, float thickness, float y, Color color)
        {
            Vector3 P(float t, float n) => new Vector3(center.x, y, center.z) + along * t + across * n;
            float back = -length * 0.5f;
            float tip = length * 0.5f;
            var a = P(back, halfWidth);
            var b = P(tip, 0f);
            var c = P(back, -halfWidth);
            var a2 = P(back + thickness, halfWidth);
            var b2 = P(tip + thickness, 0f);
            var c2 = P(back + thickness, -halfWidth);
            mb.Quad(a, a2, b2, b, color, Vector3.up);
            mb.Quad(b, b2, c2, c, color, Vector3.up);
        }

        // An ask pickup: a pink ring on the street and above it a standing pink coin with a white plus
        // on both faces. The coin is the first child, for AskPickups to spin.
        public static GameObject AskToken(Transform parent, Vector3 position, Material material)
        {
            var ground = new MeshBuilder();
            Annulus(ground, 1.9f, 2.4f, 0.05f, Pink, 24);
            var token = Emit(parent, "AskToken", ground, position, 0f, material);

            var coin = new MeshBuilder();
            const int sides = 8;
            const float r = 1.1f;
            const float half = 0.16f;
            Vector3 Rim(int i, float z) => new Vector3(Mathf.Cos(i * Mathf.PI * 2f / sides + Mathf.PI / sides) * r, Mathf.Sin(i * Mathf.PI * 2f / sides + Mathf.PI / sides) * r, z);
            for (int i = 0; i < sides; i++)
            {
                coin.Triangle(Vector3.forward * -half, Rim(i + 1, -half), Rim(i, -half), Pink, Vector3.back);
                coin.Triangle(Vector3.forward * half, Rim(i, half), Rim(i + 1, half), Pink, Vector3.forward);
                var mid = (Rim(i, 0f) + Rim(i + 1, 0f)).normalized;
                coin.Quad(Rim(i, -half), Rim(i + 1, -half), Rim(i + 1, half), Rim(i, half), DeepPink, mid);
            }
            foreach (float z in new[] { -half - 0.01f, half + 0.01f })
            {
                coin.Box(new Vector3(0f, 0f, z), new Vector3(1.15f, 0.3f, 0.02f), Quaternion.identity, White);
                coin.Box(new Vector3(0f, 0f, z), new Vector3(0.3f, 1.15f, 0.02f), Quaternion.identity, White);
            }
            var c = Emit(token.transform, "Coin", coin, position + Vector3.up * 1.6f, 0f, material);
            c.transform.SetAsFirstSibling();
            return token;
        }

        public static GameObject StartRing(Transform parent, Vector3 center, float radius, Material material)
        {
            var ring = new GameObject("StartRing");
            ring.transform.SetParent(parent, false);
            ring.transform.position = center;
            StartRingFx.Attach(ring, radius, material);
            return ring;
        }

        public static void Finish(Transform parent, Vector3 line, float heading, float halfWidth, Vector3 site, Material material)
        {
            var mb = new MeshBuilder();
            float sq = 0.8f;
            int across = Mathf.CeilToInt(halfWidth * 2f / sq);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < across; i++)
                {
                    float x = -halfWidth + i * sq;
                    float z = row * sq;
                    var color = (i + row) % 2 == 0 ? Ink : White;
                    mb.Quad(new Vector3(x, 0.06f, z), new Vector3(x, 0.06f, z + sq), new Vector3(x + sq, 0.06f, z + sq), new Vector3(x + sq, 0.06f, z), color, Vector3.up);
                }

            float poleX = halfWidth + 0.8f;
            float poleH = 6.6f;
            mb.Box(new Vector3(-poleX, poleH * 0.5f, 0.8f), new Vector3(0.3f, poleH, 0.3f), Quaternion.identity, Ink);
            mb.Box(new Vector3(poleX, poleH * 0.5f, 0.8f), new Vector3(0.3f, poleH, 0.3f), Quaternion.identity, Ink);
            float bannerSq = 0.62f;
            int cols = Mathf.CeilToInt(poleX * 2f / bannerSq);
            float start = -cols * bannerSq * 0.5f;
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < cols; i++)
                {
                    var color = (i + row) % 2 == 0 ? Ink : White;
                    mb.Box(new Vector3(start + (i + 0.5f) * bannerSq, poleH - 0.35f - row * bannerSq, 0.8f), new Vector3(bannerSq, bannerSq, 0.12f), Quaternion.identity, color);
                }
            mb.Box(new Vector3(0f, poleH + 0.02f, 0.8f), new Vector3(cols * bannerSq + 0.2f, 0.16f, 0.2f), Quaternion.identity, Yellow);
            Emit(parent, "YcFinish", mb, line, heading, material);

            var pole = new MeshBuilder();
            pole.Box(new Vector3(0f, 6.5f, 0f), new Vector3(0.28f, 13f, 0.28f), Quaternion.identity, Ink);
            pole.Box(new Vector3(0f, 13.15f, 0f), new Vector3(0.5f, 0.3f, 0.5f), Quaternion.identity, Yellow);
            var poleGo = Emit(parent, "YcFlagPole", pole, site, heading + 90f, material);

            var flag = new MeshBuilder();
            float fsq = 0.9f;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 6; c++)
                {
                    var color = (r + c) % 2 == 0 ? Ink : White;
                    float lean = c * 0.12f;
                    flag.Box(new Vector3(0.15f + (c + 0.5f) * fsq, 12.8f - (r + 0.5f) * fsq - lean, 0f), new Vector3(fsq, fsq, 0.08f), Quaternion.identity, color);
                }
            var flagGo = Emit(poleGo.transform, "YcFlag", flag, site, heading + 90f, material);
            flagGo.AddComponent<FlagSway>();
        }

        public static void TurnChevrons(Transform parent, RoutePath route, IReadOnlyList<Vector3> routeWorld, Material material)
        {
            var mb = new MeshBuilder();
            for (int i = 1; i < route.Points.Length - 1; i++)
            {
                var din = (route.Points[i] - route.Points[i - 1]).normalized;
                var dout = (route.Points[i + 1] - route.Points[i]).normalized;
                if (Vector2.Angle(din, dout) < 30f) continue;
                float d = route.Distance[i];
                for (int k = 0; k < 3; k++)
                {
                    float at = d - 16f + k * 4f;
                    if (at < 2f) continue;
                    var p = route.PointAt(at);
                    var dir = Vector2.Lerp(din, dout, 0.5f + k * 0.15f).normalized;
                    var along = new Vector3(dir.x, 0f, dir.y);
                    var acrossV = new Vector3(dir.y, 0f, -dir.x);
                    float y = routeWorld[Mathf.Min(i, routeWorld.Count - 1)].y + 0.07f;
                    Chevron(mb, new Vector3(p.x, 0f, p.y), along, acrossV, 1.6f, 1.4f, 0.55f, y, Yellow);
                }
            }
            Emit(parent, "RouteChevrons", mb, Vector3.zero, 0f, material);
        }
    }

    public sealed class FlagSway : MonoBehaviour
    {
        Quaternion rest;

        void Start() => rest = transform.localRotation;

        void Update() => transform.localRotation = rest * Quaternion.Euler(0f, Mathf.Sin(Time.time * 1.7f) * 6f, 0f);
    }
}
