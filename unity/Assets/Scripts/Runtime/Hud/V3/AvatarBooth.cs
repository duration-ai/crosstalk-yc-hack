using UnityEngine;

namespace Dogpatch.Gameplay
{
    // HUD v3, Avatar A board: the photo booth. WAIST UP brackets, an octagon shutter that counts
    // 3-2-1 and flashes (tap again to shoot now), flip and SKIP; SUITING UP with the photo pinned and
    // twelve sweeping segments; and the failure states SHOW YOUR FACE, NO SIGNAL / AGAIN and
    // CAMERA OFF / SETTINGS, each one visual, one action and a skip.
    public sealed partial class AvatarCapture
    {
        const float CountSeconds = 3f;

        static readonly Color Night = new Color32(0x22, 0x2A, 0x5A, 0xFF);
        static readonly Color NightRay = new Color32(0x2A, 0x34, 0x6C, 0xFF);

        const float SuitSeconds = 1.8f;

        float countFrom = -1f;
        float uploadAt;

        float SuitProgress => Mathf.Clamp01((Time.unscaledTime - uploadAt) / SuitSeconds);

        static readonly Color[] SuitColors =
        {
            new Color32(0xE8, 0xB0, 0x8A, 0xFF), new Color32(0x3B, 0x2A, 0x1E, 0xFF), new Color32(0xFF, 0x4F, 0x9A, 0xFF), new Color32(0x2E, 0x6C, 0xD8, 0xFF),
        };

        // Twelve segments filling with the SUITING UP progress, the lead one flickering.
        static void Sweep(Rect r, float progress)
        {
            float gap = r.width * 0.012f;
            float w = (r.width - gap * 11f) / 12f;
            int lit = Mathf.FloorToInt(progress * 12.99f);
            for (int i = 0; i < 12; i++)
            {
                var seg = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                Para(new Rect(seg.x - 1.5f, seg.y - 1.5f, seg.width + 3f, seg.height + 3f), Ink);
                bool lead = i == lit && Mathf.Repeat(Time.unscaledTime * 12f, 1f) < 0.5f;
                Para(seg, i < lit || lead ? Sunshine : White);
            }
        }
        float flashAt = -10f;
        GUIStyle big;

        bool CameraOff => stage == Stage.Failed && failure == "CAMERA OFF";

        void PressShutter()
        {
            if (countFrom < 0f)
            {
                countFrom = Time.unscaledTime;
                return;
            }
            countFrom = -1f;
            flashAt = Time.unscaledTime;
            Shoot();
        }

        void UpdateCount()
        {
            if (stage == Stage.Uploading && Time.unscaledTime - uploadAt > SuitSeconds) StartReveal();
            if (stage != Stage.Viewfinder) countFrom = -1f;
            if (countFrom < 0f || Time.unscaledTime - countFrom < CountSeconds) return;
            PressShutter();
        }

        void BoothButtons(System.Collections.Generic.List<(string, Rect)> list)
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            list.Add(("skip", new Rect(safe.xMax - 24f * s - 132f * s, safe.yMax - 24f * s - 50f * s, 120f * s, 50f * s)));
            float o = 128f * s;
            var octagon = new Rect(safe.center.x - o / 2f, safe.yMin + 48f * s, o, o);
            float ww = 300f * s, wh = 78f * s;
            var wide = new Rect(safe.center.x - ww / 2f, safe.yMin + 64f * s, ww, wh);
            switch (stage)
            {
                case Stage.Viewfinder:
                    list.Add(("shutter", octagon));
                    if (feed != null && devices.Length > 1)
                        list.Add(("flip", new Rect(safe.xMax - 40f * s - 96f * s, octagon.center.y - 36f * s, 96f * s, 72f * s)));
                    break;
                case Stage.Refused:
                    list.Add(("open", octagon));
                    break;
                case Stage.Failed:
                    list.Add((CameraOff ? "settings" : "open", wide));
                    break;
            }
        }

        void DrawBooth()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            bool tall = Screen.height > Screen.width;
            if (stage == Stage.Viewfinder || stage == Stage.Refused)
            {
                DrawPhoto();
                BeginShapes();
                Brackets(tall ? new Rect(safe.xMin + 36f * s, safe.yMin + 210f * s, safe.width - 72f * s, safe.height - 330f * s)
                    : new Rect(safe.center.x - safe.height * 0.42f, safe.yMin + 30f * s, safe.height * 0.84f, safe.height - 60f * s), White, s);
                EndShapes();
            }
            else
            {
                BeginShapes();
                if (CameraOff) Sunburst(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f), Night, NightRay);
                else Sunburst(new Vector2(Screen.width * 0.5f, Screen.height * 0.45f), stage == Stage.Uploading ? Burst : Night, stage == Stage.Uploading ? BurstRay : NightRay);
                EndShapes();
                if (!CameraOff) DrawSuiting();
            }

            if (stage == Stage.Viewfinder)
            {
                var tag = TagRect("WAIST UP");
                tag.width += 40f * s;
                BeginShapes();
                Para(tag, Sunshine);
                BustPictogram(new Vector2(tag.x + tag.height * Lean * 0.5f + 34f * s, tag.center.y), tag.height * 0.2f, Ink);
                EndShapes();
                Label(new Rect(tag.x + 44f * s, tag.y, tag.width, tag.height), "WAIST UP", Ink, 30f * s);
            }
            else if (stage == Stage.Refused)
            {
                var face = tall ? new Rect(Screen.width * 0.5f - 90f * s, Screen.height * 0.6f, 180f * s, 220f * s)
                    : new Rect(Screen.width * 0.5f - 80f * s, Screen.height * 0.5f, 160f * s, 200f * s);
                BeginShapes();
                Dashes(face, Sunshine, 5f * s);
                EndShapes();
                CenterTag("SHOW YOUR FACE", new Vector2(Screen.width * 0.5f, face.y - 70f * s), Sunshine, Ink, 34f * s);
            }
            else if (stage == Stage.Uploading)
            {
                var tag = TagRect("SUITING UP");
                tag.y = safe.center.y + (tall ? 70f : 10f) * s;
                tag.x = safe.xMin + 24f * s;
                BeginShapes();
                Para(tag, Ink);
                Sweep(new Rect(tag.x + 10f * s, tag.y - 28f * s, Mathf.Max(tag.width, 300f * s), 16f * s), SuitProgress);
                EndShapes();
                Label(tag, "SUITING UP", White, 30f * s);
            }
            else if (CameraOff)
            {
                var c = new Vector2(Screen.width * 0.5f, safe.center.y + 90f * s);
                BeginShapes();
                CameraPictogram(c, 52f * s, White);
                Line(c + new Vector2(-90f * s, -84f * s), c + new Vector2(90f * s, 96f * s), 12f * s, Night);
                Line(c + new Vector2(-84f * s, -90f * s), c + new Vector2(84f * s, 90f * s), 9f * s, White);
                EndShapes();
                CenterTag("CAMERA OFF", new Vector2(Screen.width * 0.5f, c.y - 130f * s), Sunshine, Ink, 34f * s);
            }
            else if (stage == Stage.Failed)
            {
                var plate = new Rect(Screen.width * 0.5f - 150f * s, safe.center.y - 10f * s, 300f * s, 96f * s);
                BeginShapes();
                Para(new Rect(plate.x - 4f * s, plate.y - 4f * s, plate.width + 8f * s, plate.height + 8f * s), White);
                Para(plate, Ink);
                SignalPictogram(new Vector2(plate.x + 62f * s, plate.center.y), 22f * s, Sunshine);
                EndShapes();
                Label(new Rect(plate.x + 84f * s, plate.y, plate.width, plate.height), failure == "TRY AGAIN" ? "NO SIGNAL" : failure, White, 34f * s);
            }

            BeginShapes();
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "shutter") Octagon(rect, down, countFrom < 0f ? 1f : 1f - (Time.unscaledTime - countFrom) / CountSeconds, null);
                else if (id == "open" && stage == Stage.Refused) Octagon(rect, down, 1f, r => RetakePictogram(r.center, r.height * 0.2f, Ink));
                else if (id == "open" || id == "settings")
                    Plate(rect, Sunshine, Ink, down, r => (id == "open" ? (System.Action<Vector2, float, Color>)RetakePictogram : GearPictogram)(new Vector2(r.x + r.height * 0.8f, r.center.y), r.height * 0.2f, down ? Sunshine : Ink));
                else if (id == "flip") Plate(rect, Ink, White, down, r => FlipPictogram(r.center, r.height * 0.26f, down ? Ink : White));
                else if (id == "skip")
                {
                    Plate(rect, Ink, White, down, _ => { });
                    float k = rect.height * 0.16f;
                    var c = new Vector2(rect.xMax - 26f * s, rect.center.y);
                    Chevron(c - new Vector2(k * 1.1f, 0f), k, down ? Ink : Sunshine, s);
                    Chevron(c, k, down ? Ink : Sunshine, s);
                }
            }
            EndShapes();
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "skip") Label(new Rect(rect.x - 4f * s, rect.y, rect.width, rect.height), "SKIP", down ? Ink : White, 22f * s);
                else if (id == "open" && stage == Stage.Failed) Label(new Rect(rect.x + 70f * s, rect.y, rect.width, rect.height), "AGAIN", down ? Sunshine : Ink, 32f * s);
                else if (id == "settings") Label(new Rect(rect.x + 70f * s, rect.y, rect.width, rect.height), "SETTINGS", down ? Sunshine : Ink, 32f * s);
            }

            if (countFrom >= 0f)
            {
                int n = Mathf.Clamp(Mathf.CeilToInt(CountSeconds - (Time.unscaledTime - countFrom)), 1, 3);
                float t = Mathf.Repeat(Time.unscaledTime - countFrom, 1f);
                float size = 380f * s * (1.25f - 0.25f * Mathf.Clamp01(t * 5f));
                BigNumber(n.ToString(), new Vector2(Screen.width * 0.5f, Screen.height * 0.55f), size);
            }
            float flash = 1f - (Time.unscaledTime - flashAt) / 0.4f;
            if (flash > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, flash);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        // SUITING UP and NO SIGNAL: the shot pinned top left, the racer as a dark silhouette with a
        // sunshine scan bar climbing it.
        void DrawSuiting()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            if (body != null)
            {
                bool tall = Screen.height > Screen.width;
                float h = tall ? safe.height * 0.5f : safe.height * 0.78f;
                float w = h * BodyWidth / BodyHeight;
                var area = new Rect(safe.center.x - w / 2f + (tall ? 30f : 120f) * s, safe.yMin + (tall ? 170f : 20f) * s, w, h);
                BeginShapes();
                Disc(new Vector2(area.center.x, area.yMin + h * 0.07f), w * 0.4f, w * 0.09f, Ink);
                Disc(new Vector2(area.center.x, area.yMin + h * 0.07f), w * 0.34f, w * 0.07f, Sunshine);
                Disc(new Vector2(area.center.x, area.yMin + h * 0.07f), w * 0.29f, w * 0.055f, Ink);
                EndShapes();
                GUI.color = stage == Stage.Uploading ? Ink : new Color32(0x3A, 0x48, 0x8A, 0xFF);
                float turn = stage == Stage.Uploading ? Mathf.Cos(SuitProgress * Mathf.PI * 4f) : 1f;
                float sw = w * Mathf.Max(0.12f, Mathf.Abs(turn));
                var spun = new Rect(area.center.x - sw / 2f, Screen.height - area.yMax, sw, h);
                if (turn < 0f) GUI.DrawTexture(new Rect(spun.xMax, spun.y, -sw, h), body);
                else GUI.DrawTexture(spun, body);
                GUI.color = Color.white;
                if (stage == Stage.Uploading)
                {
                    float y = area.yMin + h * Mathf.Repeat(SuitProgress * 3f, 1f);
                    BeginShapes();
                    Line(new Vector2(area.xMin - 20f * s, y), new Vector2(area.xMax + 20f * s, y), 6f * s, Sunshine);
                    EndShapes();
                }
            }
            if (still == null) return;
            float pw = 150f * s, ph = pw * still.height / Mathf.Max(1f, still.width);
            ph = Mathf.Min(ph, 210f * s);
            var photo = new Rect(safe.xMin + 30f * s, Screen.height - safe.yMax + 30f * s, pw, ph);
            if (stage == Stage.Uploading)
            {
                float fly = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(SuitProgress / 0.22f));
                float k = Mathf.Lerp(Mathf.Min(Screen.width * 0.8f / pw, Screen.height * 0.7f / ph), 1f, fly);
                var full = new Rect(Screen.width / 2f - pw * k / 2f, Screen.height / 2f - ph * k / 2f, pw * k, ph * k);
                photo = new Rect(Mathf.Lerp(full.x, photo.x, fly), Mathf.Lerp(full.y, photo.y, fly), full.width, full.height);
                pw = photo.width;
                ph = photo.height;
            }
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(-5f, photo.center);
            float b = 8f * s;
            GUI.color = Ink;
            GUI.DrawTexture(new Rect(photo.x - b - 3f * s, photo.y - b - 3f * s, pw + 2f * b + 6f * s, ph + 2f * b + 6f * s), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(photo.x - b, photo.y - b, pw + 2f * b, ph + 2f * b), Texture2D.whiteTexture);
            GUI.DrawTexture(photo, still, ScaleMode.ScaleAndCrop);
            GUI.matrix = old;
            if (stage != Stage.Uploading || SuitProgress < 0.22f) return;
            BeginShapes();
            for (int i = 0; i < SuitColors.Length; i++)
            {
                var pip = new Rect(photo.x + i * 34f * s, Screen.height - photo.yMax - 30f * s - 28f * s, 26f * s, 26f * s);
                bool done = SuitProgress > 0.3f + i * 0.15f;
                Para(new Rect(pip.x - 2f * s, pip.y - 2f * s, pip.width + 4f * s, pip.height + 4f * s), Ink);
                Para(pip, done ? SuitColors[i] : White);
                if (done) TickPictogram(pip.center, pip.height * 0.22f, White);
            }
            EndShapes();
        }

        void CenterTag(string text, Vector2 center, Color fill, Color ink, float size)
        {
            float s = Scale;
            float h = size * 1.7f, w = TextWidth(text, size) + h * Lean + 44f * s;
            var rect = new Rect(center.x - w / 2f, center.y - h / 2f, w, h);
            BeginShapes();
            Para(new Rect(rect.x - 4f * s, rect.y - 4f * s, rect.width + 8f * s, rect.height + 8f * s), Ink);
            Para(rect, fill);
            EndShapes();
            Label(rect, text, ink, size);
        }

        void BigNumber(string text, Vector2 center, float size)
        {
            if (big == null) big = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            big.fontSize = Mathf.RoundToInt(size);
            var r = new Rect(center.x - size, Screen.height - center.y - size * 0.6f, size * 2f, size * 1.2f);
            float o = size * 0.04f;
            big.normal.textColor = Ink;
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, big);
            big.normal.textColor = Sunshine;
            GUI.Label(r, text, big);
        }

        // The shutter: a sunshine octagon in an ink and white ring. While counting the fill drains.
        void Octagon(Rect rect, bool down, float fill, System.Action<Rect> pictogram)
        {
            float s = Scale;
            var c = rect.center + (down ? new Vector2(3f * s, -3f * s) : Vector2.zero);
            float r = rect.width * 0.5f * (down ? 0.96f : 1f);
            OctagonFill(c, r, Ink);
            OctagonFill(c, r - 5f * s, White);
            OctagonFill(c, r - 14f * s, Ink);
            OctagonFill(c, (r - 18f * s) * Mathf.Clamp01(fill), Sunshine);
            pictogram?.Invoke(new Rect(c.x - r, c.y - r, r * 2f, r * 2f));
        }

        static void OctagonFill(Vector2 c, float r, Color color)
        {
            if (r <= 0f) return;
            for (int i = 0; i < 8; i++)
            {
                float a0 = (i + 0.5f) * Mathf.PI / 4f, a1 = (i + 1.5f) * Mathf.PI / 4f;
                Tri(c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, color);
            }
        }

        static void Brackets(Rect r, Color color, float s)
        {
            float l = Mathf.Min(r.width, r.height) * 0.12f, t = 6f * s;
            foreach (var (corner, dx, dy) in new[] { (new Vector2(r.xMin, r.yMin), 1f, 1f), (new Vector2(r.xMax, r.yMin), -1f, 1f), (new Vector2(r.xMin, r.yMax), 1f, -1f), (new Vector2(r.xMax, r.yMax), -1f, -1f) })
            {
                Line(corner, corner + new Vector2(dx * l, 0f), t, color);
                Line(corner, corner + new Vector2(0f, dy * l), t, color);
            }
        }

        static void Dashes(Rect r, Color color, float t)
        {
            var corners = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            float dash = t * 4f;
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                float len = (b - a).magnitude;
                var n = (b - a) / len;
                for (float d = 0f; d < len; d += dash * 2f) Line(a + n * d, a + n * Mathf.Min(len, d + dash), t, color);
            }
        }

        static void Chevron(Vector2 c, float k, Color color, float s)
        {
            Line(c + new Vector2(-k * 0.5f, k), c + new Vector2(k * 0.5f, 0f), 3.5f * s, color);
            Line(c + new Vector2(k * 0.5f, 0f), c + new Vector2(-k * 0.5f, -k), 3.5f * s, color);
        }

        static void BustPictogram(Vector2 c, float k, Color stroke)
        {
            Disc(c + new Vector2(0f, k * 0.7f), k * 0.6f, k * 0.6f, stroke);
            Tri(c + new Vector2(-k * 1.3f, -k * 1.3f), c + new Vector2(k * 1.3f, -k * 1.3f), c + new Vector2(k * 0.6f, 0f), stroke);
            Tri(c + new Vector2(-k * 1.3f, -k * 1.3f), c + new Vector2(k * 0.6f, 0f), c + new Vector2(-k * 0.6f, 0f), stroke);
        }

        static void RetakePictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.3f);
            const int steps = 10;
            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(0.35f, 5.6f, i / (float)steps), a1 = Mathf.Lerp(0.35f, 5.6f, (i + 1) / (float)steps);
                Line(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * k, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * k, t, stroke);
            }
            var tip = c + new Vector2(Mathf.Cos(0.35f), Mathf.Sin(0.35f)) * k;
            Tri(tip + new Vector2(-k * 0.55f, k * 0.15f), tip + new Vector2(k * 0.45f, k * 0.25f), tip + new Vector2(0f, -k * 0.55f), stroke);
        }

        static void GearPictogram(Vector2 c, float k, Color stroke)
        {
            Ring(c, k * 0.7f, k * 0.45f, stroke);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Line(c + d * k * 0.8f, c + d * k * 1.2f, k * 0.4f, stroke);
            }
        }

        static void SignalPictogram(Vector2 c, float k, Color stroke)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = c.x - k * 1.2f + i * k * 0.7f, h = k * (0.5f + i * 0.45f);
                Line(new Vector2(x, c.y - k), new Vector2(x, c.y - k + h), k * 0.4f, i < 2 ? stroke : new Color(stroke.r, stroke.g, stroke.b, 0.3f));
            }
            Line(c + new Vector2(k * 0.6f, k * 0.9f), c + new Vector2(k * 1.4f, k * 0.1f), k * 0.25f, stroke);
            Line(c + new Vector2(k * 0.6f, k * 0.1f), c + new Vector2(k * 1.4f, k * 0.9f), k * 0.25f, stroke);
        }
    }
}
