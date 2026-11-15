using System;
using System.Collections.Generic;
using Dogpatch.Data;
using Dogpatch.Glb;
using Dogpatch.World;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Headless renders of real crossings, top-down and at street level: Tennessee/22nd, 3rd/20th,
    // Illinois/20th and the mid-block OSM crossing nearest Tennessee/22nd. Run under xvfb-run:
    //   RENDER_DIR=out Unity -batchmode -quit -projectPath unity -executeMethod Dogpatch.EditorTools.CrossingRenders.Run
    public static class CrossingRenders
    {
        public static void Run()
        {
            try
            {
                var material = WorldRenders.Material();
                var data = JsonUtility.FromJson<WorldData>(Resources.Load<TextAsset>("World/dogpatch").text);
                var models = new ModelLibrary();
                var ctx = WorldBuilder.Build(data, models, material);
                WorldRenders.Park(ctx, models, material);

                var views = new List<WorldRenders.View>();
                Vector2 anchor = Vector2.zero;
                foreach (var (name, a, b) in new[] { ("tennessee-22nd", "Tennessee Street", "22nd Street"), ("3rd-20th", "3rd Street", "20th Street"), ("illinois-20th", "Illinois Street", "20th Street") })
                {
                    if (!Junction(ctx.Roads, a, b, out var p, out var along))
                    {
                        Debug.LogWarning("[CrossingRenders] no junction of " + a + " and " + b);
                        continue;
                    }
                    if (anchor == Vector2.zero) anchor = p;
                    Debug.Log("[CrossingRenders] " + name + " at " + p);
                    Add(views, name, p, along);
                }

                var mid = Vector2.zero;
                float best = float.MaxValue;
                foreach (var cw in ctx.Surface.Crosswalks)
                {
                    float d = (cw.Center - anchor).sqrMagnitude;
                    if (!cw.MidBlock || d >= best) continue;
                    best = d;
                    mid = cw.Center;
                }
                if (best < float.MaxValue && ctx.Roads.Nearest(mid, 2f, out var hit))
                {
                    Debug.Log("[CrossingRenders] mid-block crossing on " + ctx.Roads.Roads[hit.Road].name + " at " + mid);
                    Add(views, "mid-block", mid, hit.Direction);
                }

                WorldRenders.Render(ctx, data, Environment.GetEnvironmentVariable("RENDER_DIR") ?? "../Builds/renders/crossings", views);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[CrossingRenders] " + e);
                EditorApplication.Exit(1);
            }
        }

        static void Add(List<WorldRenders.View> views, string name, Vector2 p, Vector2 along)
        {
            var at = new Vector3(p.x, 0f, p.y);
            var fwd = new Vector3(along.x, 0f, along.y).normalized;
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            views.Add(new WorldRenders.View { Name = name + "-top", Eye = at + Vector3.up * 42f - fwd * 0.5f, Target = at, Fov = 50f });
            views.Add(new WorldRenders.View { Name = name + "-street", Eye = at - fwd * 24f + right * 3f + Vector3.up * 3.2f, Target = at + Vector3.up * 0.3f, Fov = 55f });
        }

        // Where a vertex of a road named a is also a vertex of a road named b; along is a's direction there.
        static bool Junction(RoadNetwork net, string a, string b, out Vector2 point, out Vector2 along)
        {
            var onB = new HashSet<Vector2Int>();
            for (int r = 0; r < net.Roads.Length; r++)
                if (net.Roads[r].name == b)
                    foreach (var p in net.Points[r]) onB.Add(Vector2Int.RoundToInt(p * 2f));
            for (int r = 0; r < net.Roads.Length; r++)
            {
                if (net.Roads[r].name != a) continue;
                var pts = net.Points[r];
                for (int i = 0; i < pts.Length; i++)
                {
                    if (!onB.Contains(Vector2Int.RoundToInt(pts[i] * 2f))) continue;
                    point = pts[i];
                    along = (i < pts.Length - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                    return true;
                }
            }
            point = along = Vector2.zero;
            return false;
        }
    }
}
