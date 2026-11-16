using System;
using System.Collections;
using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Covers launch with a speed-dial card, "Dialling Dogpatch…", so the synchronous world build and
    // the first seconds of city streaming, Operator and crowd setup read as loading instead of a frozen
    // or stuttering world. The card draws for two frames before the GameController exists, stays up
    // until the game is ready, city streaming has finished and 15 frames in a row ran under 40 ms
    // (at most 8 s after ready), then fades. On by default; -no-loading-card or
    // DOGPATCH_NO_LOADING_CARD starts the game immediately as before.
    public sealed class LoadingCard : MonoBehaviour
    {
        const float SmoothMs = 40f;
        const int SmoothFrames = 15;
        const float MaxHoldSeconds = 8f;
        const float MaxStartSeconds = 30f;
        const float FadeSeconds = 0.35f;

        public static bool Enabled =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-no-loading-card") < 0 &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOGPATCH_NO_LOADING_CARD"));

        public static void Show(Func<GameController> start)
        {
            var card = new GameObject("Loading card").AddComponent<LoadingCard>();
            card.StartCoroutine(card.Run(start));
        }

        float alpha = 1f;
        Texture2D ring, disc;
        GUIStyle title;

        IEnumerator Run(Func<GameController> start)
        {
            yield return null;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            var game = start();
            while (game != null && !game.IsReady && Time.realtimeSinceStartup - t0 < MaxStartSeconds) yield return null;
            float ready = Time.realtimeSinceStartup;
            int smooth = 0;
            while (Time.realtimeSinceStartup - ready < MaxHoldSeconds)
            {
                yield return null;
                smooth = Time.unscaledDeltaTime * 1000f < SmoothMs ? smooth + 1 : 0;
                if (smooth >= SmoothFrames && HitchLogger.StreamingChunks <= 0) break;
            }
            Debug.Log($"[Loading] card up {Time.realtimeSinceStartup - t0:F1}s: game ready after {ready - t0:F1}s, settled {Time.realtimeSinceStartup - ready:F1}s later");
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            Destroy(gameObject);
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

        // IMGUI draws colours as given, so the linear palette goes in gamma-corrected in a linear build.
        static Color Fade(Color c, float a)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) c = c.gamma;
            c.a *= a;
            return c;
        }

        void OnGUI()
        {
            GUI.depth = -10000;
            if (Event.current.type != EventType.Repaint) return;
            ring ??= Circle(256, 0.86f);
            disc ??= Circle(64, 0f);
            float w = Screen.width, h = Screen.height, u = Mathf.Min(w, h);
            var white = Texture2D.whiteTexture;
            var matrix = GUI.matrix;

            GUI.color = Fade(SpeedDial.SkyTop, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), white);

            var c = new Vector2(w * 0.5f, h * 0.44f);
            float r = u * 0.24f;
            GUI.color = Fade(SpeedDial.Cloud, alpha);
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), ring);
            for (int i = 0; i <= 10; i++)
            {
                GUIUtility.RotateAroundPivot(-135f + 27f * i, c);
                float len = i % 5 == 0 ? r * 0.2f : r * 0.12f;
                GUI.DrawTexture(new Rect(c.x - u * 0.006f, c.y - r * 0.9f, u * 0.012f, len), white);
                GUI.matrix = matrix;
            }

            float sweep = -135f + 270f * (0.5f - 0.5f * Mathf.Cos(Time.realtimeSinceStartup * 2.4f));
            GUI.color = Fade(SpeedDial.DoubleYellow, alpha);
            GUIUtility.RotateAroundPivot(sweep, c);
            GUI.DrawTexture(new Rect(c.x - u * 0.011f, c.y - r * 0.78f, u * 0.022f, r * 0.78f), white);
            GUI.matrix = matrix;
            float hub = r * 0.14f;
            GUI.DrawTexture(new Rect(c.x - hub, c.y - hub, 2f * hub, 2f * hub), disc);

            title ??= new GUIStyle(GUI.skin.label) { font = Resources.Load<Font>("Fonts/Exo2-BlackItalic"), alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            title.fontSize = Mathf.RoundToInt(u * 0.075f);
            title.normal.textColor = Fade(SpeedDial.Cloud, alpha);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, c.y + r * 1.25f, w, u * 0.12f), "DIALLING DOGPATCH…", title);
        }
    }
}
