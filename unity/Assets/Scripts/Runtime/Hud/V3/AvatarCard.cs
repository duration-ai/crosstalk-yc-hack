using UnityEngine;

namespace Dogpatch.Gameplay
{
    // HUD v3, Avatar A board: the angular head-cropped HUD portrait and the racer card name plate
    // (sunburst, the racer standing, NAME plate with a dice for a random name and a tick).
    public sealed partial class AvatarCapture
    {
        const int BodyWidth = 256, BodyHeight = 384;

        static readonly Color Burst = new Color32(0x1F, 0x6F, 0xE0, 0xFF);
        static readonly Color BurstRay = new Color32(0x3B, 0x8C, 0xF2, 0xFF);
        static readonly Color32 Key = new Color32(0xFF, 0x00, 0xFF, 0xFF);

        static readonly string[] RandomNames =
        {
            "TURBO TACO", "PIER PILOT", "FOG RUNNER", "BAY BOLT", "SOURDOUGH SAM", "GRIDLOCK GUS",
            "HILL BOMBER", "NOODLE NITRO", "DOCK ROCKET", "CHAOS CALLER", "BRICK BANDIT", "DRIFT DOLLY",
            "SLIPSTREAM SUE", "PIXEL PETE", "CRANE GAME", "RAMP RANGER", "TOAST TORNADO", "WHARF WIZ",
        };

        Texture2D body;

        void RollName()
        {
            string next;
            do next = RandomNames[Random.Range(0, RandomNames.Length)]; while (next == typed);
            Type(next);
            if (keyboard != null) keyboard.text = typed;
        }

        // The racer card's full-body shot, keyed out of a flat background so it stands on any card.
        void RenderBody()
        {
            var root = worn.Instantiate(material);
            root.transform.SetPositionAndRotation(new Vector3(0f, 2000f, 0f), Quaternion.Euler(0f, -18f, 0f));
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PortraitLayer;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                DestroyImmediate(root);
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float height = bounds.size.y;

            var cam = new GameObject("Avatar Body Camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << PortraitLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = (Color)Key;
            cam.fieldOfView = 22f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            float distance = height * 0.56f / Mathf.Tan(11f * Mathf.Deg2Rad);
            cam.transform.position = bounds.center + new Vector3(0f, height * 0.08f, distance);
            cam.transform.LookAt(bounds.center);

            var rt = RenderTexture.GetTemporary(BodyWidth, BodyHeight, 24);
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            if (body == null) body = new Texture2D(BodyWidth, BodyHeight, TextureFormat.RGBA32, false) { name = "AvatarBody" };
            body.ReadPixels(new Rect(0, 0, BodyWidth, BodyHeight), 0, 0);
            RenderTexture.active = previous;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            DestroyImmediate(cam.gameObject);
            DestroyImmediate(root);

            var pixels = body.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.r > 220 && c.g < 50 && c.b > 220) pixels[i] = new Color32(0, 0, 0, 0);
                else pixels[i].a = 255;
            }
            body.SetPixels32(pixels);
            body.Apply();
        }

        // The HUD portrait: a leaning powder-blue window with the head render, an ink frame and the
        // player's slot colour as its leading edge. Pressed shifts down-right like every plate.
        void DrawPortrait()
        {
            float s = Scale;
            var rect = PortraitRect();
            if (pressed == "menu")
            {
                var m = rect.center + new Vector2(3f * s, -3f * s);
                rect = new Rect(m.x - rect.width * 0.46f, m.y - rect.height * 0.46f, rect.width * 0.92f, rect.height * 0.92f);
            }
            float lean = rect.height * Lean;
            var a = new Vector2(rect.xMin, rect.yMin);
            var b = new Vector2(rect.xMax - lean, rect.yMin);
            var c = new Vector2(rect.xMax, rect.yMax);
            var d = new Vector2(rect.xMin + lean, rect.yMax);
            float o = 3f * s;
            BeginShapes();
            Para(new Rect(rect.x - o, rect.y - o, rect.width - lean + o * 2f, rect.height + o * 2f), Ink);
            EndShapes();
            GUI.DrawTexture(new Rect(rect.x, Screen.height - rect.yMax, rect.width, rect.height), portrait);
            BeginShapes();
            var edge = new Vector2(3.5f * s, 0f);
            Line(a + edge, d + edge, 7f * s, PlayerColor);
            Line(a, b, 2f * s, Ink);
            Line(b, c, 2f * s, Ink);
            Line(c, d, 2f * s, Ink);
            EndShapes();
        }

        static void Sunburst(Vector2 center, Color fill, Color ray)
        {
            Tri(new Vector2(0, 0), new Vector2(Screen.width, 0), new Vector2(Screen.width, Screen.height), fill);
            Tri(new Vector2(0, 0), new Vector2(Screen.width, Screen.height), new Vector2(0, Screen.height), fill);
            const int rays = 18;
            float reach = Mathf.Max(Screen.width, Screen.height) * 1.6f;
            float spin = Time.unscaledTime * 0.04f;
            for (int i = 0; i < rays; i++)
            {
                float a0 = spin + i * Mathf.PI * 2f / rays, a1 = a0 + Mathf.PI / rays;
                Tri(center, center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * reach, center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * reach, ray);
            }
        }

        // The racer standing on the card, feet on a flat ring like a turntable.
        void DrawRacer(Rect area)
        {
            if (body == null) return;
            float k = Mathf.Min(area.width / BodyWidth, area.height / BodyHeight);
            float w = BodyWidth * k, h = BodyHeight * k;
            var feet = new Vector2(area.center.x, area.yMin + h * 0.07f);
            BeginShapes();
            Disc(feet, w * 0.4f, w * 0.09f, Ink);
            Disc(feet, w * 0.34f, w * 0.07f, Sunshine);
            Disc(feet, w * 0.29f, w * 0.055f, Ink);
            EndShapes();
            GUI.DrawTexture(new Rect(area.center.x - w / 2f, Screen.height - (area.yMin + h), w, h), body);
        }

        static void Disc(Vector2 c, float rx, float ry, Color color)
        {
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Tri(c, c + new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry), c + new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry), color);
            }
        }

        // Racer card name plate: the racer on a sunburst, a NAME tag over a white leaning plate,
        // the dice rolls a name, the tick saves (an empty plate keeps the current name).
        void DrawName()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            var field = NameField();
            bool tall = Screen.height > Screen.width;
            BeginShapes();
            Sunburst(new Vector2(Screen.width * 0.5f, Screen.height * 0.62f), Burst, BurstRay);
            EndShapes();
            var racer = tall
                ? new Rect(safe.xMin + 12f * s, field.y - 40f * s, field.x - safe.xMin + 40f * s, safe.yMax - field.y - 30f * s)
                : new Rect(safe.xMin + 40f * s, safe.yMin + 30f * s, field.x - safe.xMin - 60f * s, safe.height - 60f * s);
            DrawRacer(racer);

            var tag = new Rect(field.x + 14f * s, field.yMax - 10f * s, TextWidth("NAME", 22f * s) + 48f * s, 40f * s);
            BeginShapes();
            Para(new Rect(field.x - 5f * s, field.y - 5f * s, field.width + 10f * s, field.height + 10f * s), Ink);
            Para(field, White);
            Para(tag, Ink);
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "ok") Plate(rect, Sunshine, Ink, down, r => TickPictogram(r.center, r.height * 0.26f, down ? Sunshine : Ink));
                else if (id == "dice") Plate(rect, Ink, White, down, r => DicePictogram(r.center, r.height * 0.24f, down ? Ink : Sunshine));
            }
            EndShapes();
            Label(tag, "NAME", White, 22f * s);
            bool caret = typed.Length < MaxName && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
            string shown = typed.Length > 0 ? typed + (caret ? "|" : "") : (caret ? "|" : "") + placeholder;
            Label(field, shown, typed.Length > 0 ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.35f), 44f * s);
        }

        static void DicePictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.24f);
            float lean = k * 0.3f;
            var bl = c + new Vector2(-k * 1.1f - lean * 0.5f, -k * 1.1f);
            var br = c + new Vector2(k * 1.1f - lean * 0.5f, -k * 1.1f);
            var tr = c + new Vector2(k * 1.1f + lean * 0.5f, k * 1.1f);
            var tl = c + new Vector2(-k * 1.1f + lean * 0.5f, k * 1.1f);
            Line(bl, br, t, stroke);
            Line(br, tr, t, stroke);
            Line(tr, tl, t, stroke);
            Line(tl, bl, t, stroke);
            foreach (float f in new[] { -0.5f, 0f, 0.5f })
                Disc(c + new Vector2(f * k + f * lean * 0.5f, f * k), k * 0.2f, k * 0.2f, stroke);
        }
    }
}
