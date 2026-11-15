using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dogpatch.Gameplay;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Headless before / during / after shots of Operator effects from the real game scene, inert
    // unless the player is launched with
    //   -effectCapture <dir> -effectIds more_traffic,panic,... [-effectStrength medium]
    // Each effect is applied through OperatorEffects exactly as an activeEffects row would be, then
    // the row leaves the list and the revert is shot too: <id>-0-before, -1-during (1 s in),
    // -2-during (4 s in), -3-after (3 s after the revert).
    public sealed class EffectCapture : MonoBehaviour
    {
        string output;
        string[] ids;
        string strength = "medium";
        int serial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-effectCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            var capture = new GameObject("EffectCapture").AddComponent<EffectCapture>();
            capture.output = args[i + 1];
            int j = Array.IndexOf(args, "-effectIds");
            capture.ids = j >= 0 && j + 1 < args.Length ? args[j + 1].Split(',') : EffectRegistry.Built;
            int k = Array.IndexOf(args, "-effectStrength");
            if (k >= 0 && k + 1 < args.Length) capture.strength = args[k + 1];
            Application.runInBackground = true;
        }

        // Scope, class and theme each effect is shot with; object effects pick the nearest
        // matching thing in view.
        static (string scope, string cls, string theme) Setup(string id) => id switch
        {
            "speed_up" or "slow_down" => ("class", "vehicle", null),
            "ped_theme" => ("world", null, "elvis"),
            "extinguish" or "see_through" or "rock_solid" or "ghost" => ("object", "building", null),
            "bouncy" or "soft" or "icy" => ("class", "car", null),
            _ => ("world", null, null),
        };

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            GameController game;
            OperatorEffects effects;
            while ((game = FindAnyObjectByType<GameController>()) == null || game.World == null || game.Player == null || (effects = FindAnyObjectByType<OperatorEffects>()) == null || effects.Context == null) yield return null;

            var cam = game.View;
            if (game.Follow != null) game.Follow.enabled = false;
            var fx = cam.GetComponent<CameraFx>();
            if (fx != null) fx.enabled = false;
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.fieldOfView = 55f;

            var spawn = game.Player.transform.position;
            var at = new Vector2(spawn.x, spawn.z);
            var dir = Vector2.up;
            if (game.World.Roads.Nearest(at, 60f, out var hit))
            {
                at = hit.Point;
                dir = hit.Direction.normalized;
            }
            var side = new Vector2(-dir.y, dir.x);
            var look = new Vector3(at.x + dir.x * 14f, 1.5f, at.y + dir.y * 14f);
            var eye = new Vector3(at.x - dir.x * 16f + side.x * 9f, 10f, at.y - dir.y * 16f + side.y * 9f);
            game.Player.transform.position = new Vector3(at.x + side.x * 7f, spawn.y, at.y + side.y * 7f);
            for (float t = 0f; t < 12f; t += Time.deltaTime)
            {
                cam.transform.position = eye;
                cam.transform.LookAt(look);
                yield return null;
            }

            foreach (var id in ids)
            {
                var (scope, cls, theme) = Setup(id);
                var row = new ActiveEffect
                {
                    id = "capture-" + id + "-" + serial++, effect = id, scope = scope, targetClass = cls ?? "", strength = strength, theme = theme ?? "", color = "",
                    status = "active", startsAt = effects.ServerNow(), endsAt = effects.ServerNow() + 60000, label = id, instant = id == "extinguish",
                };
                if (scope == "object")
                {
                    EntityTagger.TagDynamic(game, effects.Context.Race);
                    var target = Nearest(cam, look, cls);
                    if (target == null)
                    {
                        Debug.LogWarning("[EffectCapture] no " + cls + " in view for " + id);
                        continue;
                    }
                    row.target = target.Id;
                    row.targetClass = "";
                    if (id == "extinguish")
                    {
                        var fire = new ActiveEffect { id = "capture-fire-" + serial++, effect = "set_fire", scope = "object", target = target.Id, strength = strength, status = "active", startsAt = row.startsAt, endsAt = row.endsAt };
                        effects.OnActiveList(new[] { fire });
                        yield return Hold(cam, eye, look, 2f);
                        yield return Shoot(rt, id + "-0-before");
                        effects.OnActiveList(new ActiveEffect[0]);
                        effects.OnActiveList(new[] { row });
                        yield return Hold(cam, eye, look, 0.6f);
                        yield return Shoot(rt, id + "-1-during");
                        yield return Hold(cam, eye, look, 2.5f);
                        yield return Shoot(rt, id + "-3-after");
                        continue;
                    }
                }
                yield return Hold(cam, eye, look, 1f);
                yield return Shoot(rt, id + "-0-before");
                effects.OnActiveList(new[] { row });
                yield return Hold(cam, eye, look, 1f);
                yield return Shoot(rt, id + "-1-during");
                yield return Hold(cam, eye, look, 3f);
                yield return Shoot(rt, id + "-2-during");
                effects.OnActiveList(new ActiveEffect[0]);
                yield return Hold(cam, eye, look, 3f);
                yield return Shoot(rt, id + "-3-after");
                yield return Hold(cam, eye, look, 2f);
            }
            Debug.Log("[EffectCapture] done");
            Application.Quit();
        }

        static EntityTag Nearest(Camera cam, Vector3 look, string cls)
        {
            EntityTag best = null;
            float bestD = float.MaxValue;
            foreach (var tag in EntityTag.All)
            {
                if (tag.Protected || !EntityClasses.IsA(tag.Class, cls)) continue;
                var c = tag.WorldBounds.center;
                var v = cam.WorldToViewportPoint(c);
                if (v.z < 0f || v.x < 0.15f || v.x > 0.85f || v.y < 0.1f || v.y > 0.9f) continue;
                float d = (c - look).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = tag;
                }
            }
            return best;
        }

        static IEnumerator Hold(Camera cam, Vector3 eye, Vector3 look, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                cam.transform.position = eye;
                cam.transform.LookAt(look);
                yield return null;
            }
        }

        IEnumerator Shoot(RenderTexture rt, string name)
        {
            yield return new WaitForEndOfFrame();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("[EffectCapture] " + name);
        }
    }
}
