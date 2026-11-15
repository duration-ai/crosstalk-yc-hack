using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Dogpatch.Gameplay;
using Unity.Profiling;
using UnityEngine;

namespace Dogpatch.Look
{
    // Headless look review, inert unless the player is launched with
    //   -lookCapture <dir> [-lookSet refs|grid|all] [-lookPortrait]
    // Renders fixed views of the real game scene (the game's own camera, sky, fog, LOD and lighting,
    // without the HUD) to PNG, and writes views.csv with each view's draw calls and triangles. refs
    // are views from the driving follow camera's pose (10 m back, pitched about 14 degrees down) on
    // the reference streets, plus aerials; grid samples the nearest street
    // every 240 m across the whole map at chase-camera height. Combine with -dogpatch-env for dusk,
    // night or weather.
    public sealed class LookCapture : MonoBehaviour
    {
        struct View
        {
            public string Name;
            public Vector3 Eye;
            public Vector3 Target;
        }

        string output;
        string set;
        bool portrait;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-lookCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var capture = new GameObject("LookCapture").AddComponent<LookCapture>();
            capture.output = args[i + 1];
            int j = Array.IndexOf(args, "-lookSet");
            capture.set = j >= 0 && j + 1 < args.Length ? args[j + 1] : "all";
            capture.portrait = Array.IndexOf(args, "-lookPortrait") >= 0;
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            GameController game;
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null) yield return null;
            for (int k = 0; k < 30; k++) yield return null;
            if (Race.RaceDirector.Instance != null) Destroy(Race.RaceDirector.Instance);

            var cam = game.View;
            if (game.Follow != null) game.Follow.enabled = false;
            var fx = cam.GetComponent<CameraFx>();
            if (fx != null) fx.enabled = false;
            var rt = portrait ? new RenderTexture(720, 1560, 24) { antiAliasing = 4 } : new RenderTexture(1560, 720, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.fieldOfView = portrait ? 72f : 55f;
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var csv = new StringBuilder("view,eye_x,eye_y,eye_z,draws,tris\n");

            foreach (var view in Views(game))
            {
                cam.transform.position = view.Eye;
                cam.transform.LookAt(view.Target);
                for (int k = 0; k < 8; k++) yield return null;
                yield return new WaitForEndOfFrame();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(output, view.Name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                long d = draws.Valid ? draws.LastValue : -1, t = tris.Valid ? tris.LastValue : -1;
                csv.Append(string.Format(CultureInfo.InvariantCulture, "{0},{1:F0},{2:F0},{3:F0},{4},{5}\n", view.Name, view.Eye.x, view.Eye.y, view.Eye.z, d, t));
                Debug.Log($"[LookCapture] {view.Name} draws={d} tris={t}");
            }
            File.WriteAllText(Path.Combine(output, "views.csv"), csv.ToString());
            draws.Dispose();
            tris.Dispose();
            Debug.Log("[LookCapture] done");
            Application.Quit();
        }

        IEnumerable<View> Views(GameController game)
        {
            var data = game.Data;
            var roads = game.World.Roads;
            Vector2 Local(double lat, double lon) => new Vector2(
                (float)((lon - data.originLon) * Math.Cos(data.originLat * Math.PI / 180) * 111194.93),
                (float)((lat - data.originLat) * 111194.93));

            // The chase camera behind a car on the street segment nearest 'near', looking along it.
            View Chase(string name, Vector2 near, Vector2 toward, string street = null)
            {
                float best = float.MaxValue;
                Vector2 a = near, d = toward.normalized;
                for (int r = 0; r < roads.Roads.Length; r++)
                {
                    var road = roads.Roads[r];
                    if (street != null ? road.name != street : road.kind == "path" || road.kind == "service" || road.kind == "footway") continue;
                    var pts = roads.Points[r];
                    for (int i = 0; i < pts.Length - 1; i++)
                    {
                        if (Vector2.Distance(pts[i], pts[i + 1]) < 15f) continue;
                        var mid = (pts[i] + pts[i + 1]) * 0.5f;
                        float dist = Vector2.Distance(mid, near);
                        if (dist >= best) continue;
                        best = dist;
                        a = mid;
                        d = (pts[i + 1] - pts[i]).normalized;
                        if (Vector2.Dot(d, toward) < 0f) d = -d;
                    }
                }
                var eye = a - d * 9.6f;
                return new View { Name = name, Eye = new Vector3(eye.x, 4.4f, eye.y), Target = new Vector3(a.x, 1.9f, a.y) };
            }

            var yc = Local(37.76078, -122.38664);
            var caltrain = Local(37.75761, -122.39263);
            if (set == "refs" || set == "all")
            {
                yield return Chase("ref-tennessee-st", new Vector2(yc.x - 330f, yc.y - 260f), Vector2.up, "Tennessee Street");
                yield return Chase("ref-3rd-st", new Vector2(yc.x - 150f, yc.y - 120f), Vector2.up, "3rd Street");
                yield return Chase("ref-22nd-st-east", caltrain + new Vector2(120f, 0f), Vector2.right, "22nd Street");
                yield return Chase("ref-20th-st-east", new Vector2(yc.x - 300f, yc.y - 40f), Vector2.right, "20th Street");
                yield return Chase("ref-illinois-st", new Vector2(yc.x + 40f, yc.y - 200f), Vector2.down, "Illinois Street");
                yield return new View { Name = "ref-pier-70", Eye = new Vector3(yc.x + 150f, 55f, yc.y - 120f), Target = new Vector3(yc.x - 10f, 4f, yc.y + 10f) };
                yield return new View { Name = "aerial-se", Eye = new Vector3(yc.x + 260f, 260f, yc.y - 520f), Target = new Vector3(yc.x - 180f, 0f, yc.y - 60f) };
                var b = game.World.Bounds;
                yield return new View { Name = "aerial-center", Eye = new Vector3(b.center.x, 420f, b.center.y - 700f), Target = new Vector3(b.center.x, 0f, b.center.y) };
                yield return new View { Name = "aerial-west-edge", Eye = new Vector3(b.xMin + 250f, 160f, b.center.y + 100f), Target = new Vector3(b.xMin - 200f, 0f, b.center.y + 150f) };
                yield return new View { Name = "aerial-north", Eye = new Vector3(b.center.x - 100f, 200f, b.yMax - 50f), Target = new Vector3(b.center.x, 0f, b.yMax - 450f) };
            }
            if (set == "grid" || set == "all")
            {
                var b = game.World.Bounds;
                int n = 0;
                for (float z = b.yMin + 120f; z < b.yMax; z += 240f)
                    for (float x = b.xMin + 120f; x < b.xMax; x += 240f, n++)
                    {
                        var dirs = new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
                        var view = Chase($"grid-{Mathf.RoundToInt(x)}_{Mathf.RoundToInt(z)}", new Vector2(x, z), dirs[n % 4]);
                        if (Vector2.Distance(new Vector2(view.Eye.x, view.Eye.z), new Vector2(x, z)) < 170f) yield return view;
                    }
            }
        }
    }
}
