using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.HudV3
{
    // HUD v3 board 5: the direction arrow leaves the middle of the screen and lies on the road as a
    // collar, a flat chevron on a ring of 12 dashes around your car (4.2 m, so the chevron clears the
    // bonnet from the chase camera) or your feet (1.8 m), turned toward the next stretch of the route.
    public static class GroundCollar
    {
        public const float Radius = 3f;
        const float Lift = 0.06f;

        public static void Place(Transform chevron, Transform ring, Vector3 focus, float yaw, float shown, bool driving)
        {
            float radius = driving ? Radius * 1.4f : Radius * 0.6f;
            var ground = new Vector3(focus.x, focus.y + Lift, focus.z);
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            chevron.SetPositionAndRotation(ground + dir * radius, Quaternion.Euler(0f, yaw, 0f));
            float size = 0.55f * shown * (driving ? 1f : 0.7f);
            chevron.localScale = new Vector3(size, 0.25f * shown, size);
            ring.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));
            float k = radius / Radius * shown;
            ring.localScale = new Vector3(k, 1f, k);
        }

        // Twelve flat dashes on a 3 m circle, sunshine over an ink rim. Colours are linear like the
        // rest of the vertex-coloured world.
        public static Mesh Ring(Color fill, Color edge)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            void Arc(float a0, float a1, float r0, float r1, float y, Color c)
            {
                const int steps = 4;
                int start = vertices.Count;
                for (int i = 0; i <= steps; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)steps);
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    vertices.Add(d * r0 + Vector3.up * y);
                    vertices.Add(d * r1 + Vector3.up * y);
                    colors.Add(c);
                    colors.Add(c);
                }
                for (int i = 0; i < steps; i++)
                {
                    int v = start + i * 2;
                    triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
                }
            }
            for (int i = 0; i < 12; i++)
            {
                float mid = i * Mathf.PI * 2f / 12f + Mathf.PI / 12f;
                float half = Mathf.PI / 12f * 0.55f;
                Arc(mid - half - 0.012f, mid + half + 0.012f, Radius - 0.14f, Radius + 0.14f, 0f, edge.linear);
                Arc(mid - half, mid + half, Radius - 0.09f, Radius + 0.09f, 0.01f, fill.linear);
            }
            var mesh = new Mesh { name = "collar-ring" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
