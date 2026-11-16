using System;
using System.Collections;
using System.IO;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Covers launch with the FOUNDER MODE card (handoffs/hud-v3/6-Map-pause-loading.png, screen 7):
    // a rotary dial on the sky whose 12 holes fill as loading progresses, a pink centre with her
    // handset, FOUNDER MODE in the house type and one status line, "DIALLING DOGPATCH", so the
    // synchronous world build and the first seconds of city streaming, Operator and crowd setup read
    // as loading instead of a frozen or stuttering world. The card draws for two frames before the
    // GameController exists, stays up until the game is ready, city streaming has finished and 15
    // frames in a row ran under 40 ms (at most 8 s after ready), then fades. On by default;
    // -no-loading-card or DOGPATCH_NO_LOADING_CARD starts the game immediately as before.
    // -loading-capture <file.png> saves one still of the card while it is up.
    public sealed class LoadingCard : MonoBehaviour
    {
        const float SmoothMs = 40f;
        const int SmoothFrames = 15;
        const float MaxHoldSeconds = 8f;
        const float MaxStartSeconds = 30f;
        const float FadeSeconds = 0.35f;
        const int Holes = 12;

        static readonly Color Ink = SpeedDial.Hex("#1C2347");
        static readonly Color Sunshine = SpeedDial.Hex("#FFCB1F");
        static readonly Color Pink = SpeedDial.Hex("#FF3E8E");
        static readonly Color SkyTop = SpeedDial.Hex("#0B6FD6");
        static readonly Color SkyMid = SpeedDial.Hex("#1C9BF2");
        static readonly Color SkyLow = SpeedDial.Hex("#B4E2FC");

        public static bool Enabled =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-no-loading-card") < 0 &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOGPATCH_NO_LOADING_CARD"));

        public static void Show(Func<GameController> start)
        {
            var card = new GameObject("Loading card").AddComponent<LoadingCard>();
            card.StartCoroutine(card.Run(start));
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-loading-capture");
            if (at >= 0 && at + 1 < args.Length) card.StartCoroutine(card.Capture(args[at + 1]));
        }

        float alpha = 1f;
        float progress;
        Texture2D ring, disc;
        GUIStyle title, status;

        IEnumerator Run(Func<GameController> start)
        {
            yield return null;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            var game = start();
            while (game != null && !game.IsReady && Time.realtimeSinceStartup - t0 < MaxStartSeconds)
            {
                progress = 0.6f * (1f - Mathf.Exp(-(Time.realtimeSinceStartup - t0) / 2f));
                yield return null;
            }
            float ready = Time.realtimeSinceStartup;
            float atReady = Mathf.Max(progress, 0.6f);
            int smooth = 0;
            while (Time.realtimeSinceStartup - ready < MaxHoldSeconds)
            {
                yield return null;
                smooth = Time.unscaledDeltaTime * 1000f < SmoothMs ? smooth + 1 : 0;
                float settled = Mathf.Max((float)smooth / SmoothFrames, (Time.realtimeSinceStartup - ready) / MaxHoldSeconds);
                progress = Mathf.Max(progress, Mathf.Lerp(atReady, 0.95f, settled));
                if (smooth >= SmoothFrames && HitchLogger.StreamingChunks <= 0) break;
            }
            progress = 1f;
            Debug.Log($"[Loading] card up {Time.realtimeSinceStartup - t0:F1}s: game ready after {ready - t0:F1}s, settled {Time.realtimeSinceStartup - ready:F1}s later");
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            Destroy(gameObject);
        }

        IEnumerator Capture(string path)
        {
            while (progress < 0.4f) yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log($"[Loading] captured {path} at {Screen.width}x{Screen.height}");
        }

        void OnDestroy()
        {
            if (ring != null) Destroy(ring);
            if (disc != null) Destroy(disc);
        }

        static Texture2D Circle(int size, float inner)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            float half = size * 0.5f, edge = 1.5f / half;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                float a = Mathf.Clamp01((1f - d) / edge) * (inner > 0f ? Mathf.Clamp01((d - inner) / edge) : 1f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        Color A(Color c)
        {
            c.a *= alpha;
            return c;
        }

        void Fill(Rect r, Color c)
        {
            GUI.color = A(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        void Dot(Vector2 c, float r, Color color)
        {
            GUI.color = A(color);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), disc);
        }

        void Ring(Vector2 c, float r, Color color)
        {
            GUI.color = A(color);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), ring);
        }

        // A box leaning forward like the house type: the top edge sits further right than the bottom.
        void Slanted(Rect r, Color c, float lean)
        {
            var matrix = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -lean;
            var pivot = new Vector3(r.center.x, r.center.y, 0f);
            GUI.matrix = matrix * Matrix4x4.Translate(pivot) * shear * Matrix4x4.Translate(-pivot);
            Fill(r, c);
            GUI.matrix = matrix;
        }

        void Cloud(Vector2 c, float w)
        {
            var white = Color.white;
            Fill(new Rect(c.x - w * 0.5f, c.y - w * 0.08f, w, w * 0.16f), white);
            Dot(c + new Vector2(-w * 0.2f, -w * 0.08f), w * 0.16f, white);
            Dot(c + new Vector2(w * 0.05f, -w * 0.14f), w * 0.22f, white);
            Dot(c + new Vector2(w * 0.3f, -w * 0.06f), w * 0.13f, white);
        }

        void Outlined(Rect r, string text, GUIStyle style, Color fill, float o)
        {
            style.normal.textColor = A(Ink);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o, r.width, r.height), text, style);
            }
            GUI.Label(new Rect(r.x + o * 1.6f, r.y + o * 2.4f, r.width, r.height), text, style);
            style.normal.textColor = A(fill);
            GUI.Label(r, text, style);
        }

        void OnGUI()
        {
            GUI.depth = -10000;
            if (Event.current.type != EventType.Repaint) return;
            ring ??= Circle(256, 0.86f);
            disc ??= Circle(128, 0f);
            float w = Screen.width, h = Screen.height, u = Mathf.Min(w, h);
            bool landscape = w > h;
            var matrix = GUI.matrix;
            float now = Time.realtimeSinceStartup;

            const int Bands = 24;
            for (int i = 0; i < Bands; i++)
            {
                float t = i / (Bands - 1f);
                var c = t < 0.6f ? Color.Lerp(SkyTop, SkyMid, t / 0.6f) : Color.Lerp(SkyMid, SkyLow, (t - 0.6f) / 0.4f);
                Fill(new Rect(0f, h * i / Bands, w, h / Bands + 1f), c);
            }

            float drift = now * u * 0.012f;
            Cloud(new Vector2(w * 0.18f, h * 0.16f), u * 0.24f);
            Cloud(new Vector2((w * 0.85f + drift) % (w + u * 0.3f), h * 0.27f), u * 0.2f);
            Cloud(new Vector2(w * 0.12f, h * 0.86f), u * 0.3f);
            Cloud(new Vector2(w * 0.9f, h * 0.93f), u * 0.34f);

            var dial = landscape ? new Vector2(w * 0.3f, h * 0.5f) : new Vector2(w * 0.5f, h * 0.42f);
            float r = u * 0.3f;

            for (int i = 0; i < 2; i++)
            {
                float arc = r * (1.18f + 0.14f * i);
                for (int k = -3; k <= 3; k++)
                {
                    float a = (k * 7f + Mathf.Sin(now * 3f) * 4f) * Mathf.Deg2Rad;
                    Dot(dial + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * arc, u * 0.007f, Color.white);
                }
            }

            Dot(dial + new Vector2(u * 0.012f, u * 0.016f), r * 1.04f, new Color(Ink.r, Ink.g, Ink.b, 0.35f));
            Dot(dial, r * 1.04f, Ink);
            Dot(dial, r * 0.98f, Color.white);

            int lit = Mathf.FloorToInt(progress * Holes + 0.001f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 8f);
            float spin = -18f * Mathf.Sin(now * 1.6f) * (1f - progress);
            for (int i = 0; i < Holes; i++)
            {
                float a = (-60f + spin + i * (300f / (Holes - 1))) * Mathf.Deg2Rad;
                var p = dial + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.72f;
                float hole = r * 0.15f;
                Dot(p, hole, Ink);
                var fill = i < lit ? Sunshine : i == lit && progress < 1f ? Color.Lerp(Color.white, Sunshine, pulse * 0.6f) : Color.white;
                Dot(p, hole - u * 0.007f, fill);
            }

            float stopA = 50f * Mathf.Deg2Rad;
            var stop = dial + new Vector2(Mathf.Cos(stopA), Mathf.Sin(stopA)) * r * 0.9f;
            GUIUtility.RotateAroundPivot(50f + 90f, stop);
            Fill(new Rect(stop.x - u * 0.008f, stop.y - r * 0.12f, u * 0.016f, r * 0.24f), Ink);
            GUI.matrix = matrix;

            float centre = r * 0.42f;
            Dot(dial, centre + u * 0.008f, Ink);
            Dot(dial, centre, Pink);
            Ring(dial, centre * 0.84f, new Color(1f, 1f, 1f, 0.35f));

            GUIUtility.RotateAroundPivot(-35f, dial);
            float hw = centre * 0.95f, ht = centre * 0.18f;
            Fill(new Rect(dial.x - hw * 0.5f, dial.y - ht * 0.5f + centre * 0.08f, hw, ht), Color.white);
            Fill(new Rect(dial.x - hw * 0.62f, dial.y - centre * 0.08f, ht * 1.3f, centre * 0.44f), Color.white);
            Fill(new Rect(dial.x + hw * 0.62f - ht * 1.3f, dial.y - centre * 0.08f, ht * 1.3f, centre * 0.44f), Color.white);
            GUI.matrix = matrix;
            for (int i = 0; i < 3; i++)
            {
                float a = (-70f + i * 20f) * Mathf.Deg2Rad;
                var arc = dial + new Vector2(centre * 0.3f, -centre * 0.25f);
                Dot(arc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * centre * 0.5f, u * 0.0075f, Color.white);
            }

            var font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
            title ??= new GUIStyle(GUI.skin.label) { font = font, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            status ??= new GUIStyle(GUI.skin.label) { font = font, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };

            float tx = landscape ? w * 0.5f : 0f, tw = landscape ? w * 0.48f : w;
            float size = u * (landscape ? 0.15f : 0.135f);
            float textTop = landscape ? h * 0.5f - size * 1.6f : dial.y + r * 1.42f;
            title.fontSize = Mathf.RoundToInt(size);
            float o = Mathf.Max(1.5f, u * 0.008f);
            float titleCentre = tx + tw * 0.5f;
            for (int i = 0; i < 3; i++)
                Slanted(new Rect(titleCentre - size * 3.2f - i * size * 0.12f, textTop + size * (0.35f + i * 0.17f), size * (0.9f - i * 0.18f), size * 0.07f), Sunshine, 0.5f);
            Outlined(new Rect(tx, textTop, tw, size * 1.05f), "FOUNDER", title, Sunshine, o);
            Outlined(new Rect(tx + size * 0.3f, textTop + size * 0.95f, tw, size * 1.05f), "MODE", title, Sunshine, o);

            float plateW = u * 0.62f, plateH = u * 0.085f;
            var plate = new Rect(titleCentre - plateW * 0.5f, textTop + size * 2.35f, plateW, plateH);
            Slanted(plate, Ink, 0.35f);
            status.fontSize = Mathf.RoundToInt(plateH * 0.42f);
            status.normal.textColor = A(Color.white);
            GUI.color = Color.white;
            GUI.Label(new Rect(plate.x - plateH * 0.3f, plate.y, plate.width, plate.height), "DIALLING DOGPATCH", status);
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)(now * 3f) % 3 == i;
                Slanted(new Rect(plate.xMax - plateH * (1.5f - i * 0.38f), plate.y + plateH * 0.42f, plateH * 0.24f, plateH * 0.16f), on ? Pink : new Color(Pink.r, Pink.g, Pink.b, 0.45f), 0.35f);
            }
            GUI.color = Color.white;
        }
    }
}
