using System;
using System.Collections;
using System.IO;
using Dogpatch.Race;
using Dogpatch.Operator.Voice;
using UnityEngine;

namespace Dogpatch.Operator
{
    // The phone call card (handoffs/style 08 and 09): whenever the mic is open, from a race phone
    // pickup or the HUD phone button, a stylised phone slides in from the left edge with an overshoot.
    // It shows the Operator, LISTENING… with a pulsing mic, a live waveform from the mic level and a
    // ring that drains over the listening window, then THINKING… with what she heard, then her reply
    // with a pop (or a playful decline with a shake), and slides out. A pink edge tint pulses while
    // she listens. It reads CallController's state and events only and stays in the left third, clear
    // of the screen centre and the racing line. IMGUI, ink / sunshine / hotline pink.
    public sealed class PhoneCallCard : MonoBehaviour
    {
        const float ListenWindow = 5f;
        const float SlideIn = 0.32f;
        const float SlideOut = 0.24f;
        const int Bars = 18;

        enum Phase { Hidden, Listening, Thinking, Reply, Decline }

        static readonly Color Ink = Hex("#1C2347");
        static readonly Color Sun = Hex("#FFCB1F");
        static readonly Color Pink = Hex("#FF3E8E");
        static readonly Color Paper = Hex("#F7F8F9");
        static readonly Color Skin = Hex("#D9A27A");
        const float Lean = 0.12f;

        // True while the card is on screen, so the small hearing bar stands down.
        public static bool Showing { get; private set; }

        CallController calls;
        Font font;
        GUIStyle style;
        float s;
        readonly float[] levels = new float[Bars];
        int levelAt;
        float nextLevel;

        Phase phase = Phase.Hidden;
        Phase shown = Phase.Hidden;
        float phaseAt = -10f;
        float inAt = -10f;
        float outAt = -10f;
        bool refused;
        string transcript = "";
        string line = "";

        // Preview for headless stills (-call-card-capture <dir>): overrides the live state.
        bool preview;
        Phase previewPhase;
        float previewSince;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PhoneCallCard>() != null) return;
            var go = new GameObject("Phone call card");
            DontDestroyOnLoad(go);
            var card = go.AddComponent<PhoneCallCard>();
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-call-card-capture");
            if (at >= 0) card.StartCoroutine(card.Capture(at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "call-card")));
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static float Overshoot(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * (3.2f * t + 2.2f);
        }

        void Awake() => font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");

        void OnDestroy()
        {
            if (calls != null) calls.Answered -= OnAnswered;
        }

        void OnAnswered(OperatorResponse reply) => refused = reply != null && (reply.refusal != null || reply.outcome == "refused" || reply.outcome == "declined");

        Phase Live()
        {
            if (preview) return previewPhase;
            if (calls == null) return Phase.Hidden;
            float now = Time.time;
            if (calls.State == CallState.Talking) return Phase.Listening;
            if (calls.State == CallState.Waiting) return Phase.Thinking;
            if (calls.State == CallState.Answered) return refused ? Phase.Decline : Phase.Reply;
            if (now - calls.TurnedDownAt < 2.2f) return Phase.Decline;
            return Phase.Hidden;
        }

        void Update()
        {
            if (calls == null && RaceDirector.Instance != null && RaceDirector.Instance.Calls != null)
            {
                calls = RaceDirector.Instance.Calls;
                calls.Answered += OnAnswered;
            }
            var live = Live();
            if (live != phase)
            {
                if (phase == Phase.Hidden) inAt = Time.time;
                if (live == Phase.Hidden) outAt = Time.time;
                else
                {
                    shown = live;
                    phaseAt = preview ? Time.time - previewSince : Time.time;
                }
                phase = live;
            }
            if (!preview && calls != null)
            {
                if (phase == Phase.Listening) transcript = calls.Partial;
                else if (phase == Phase.Thinking) transcript = string.IsNullOrEmpty(calls.Heard) ? calls.Partial : calls.Heard;
                if (phase == Phase.Reply || phase == Phase.Decline) line = calls.State == CallState.Answered ? calls.Line : CallController.NoAsksLine;
            }
            if (Time.unscaledTime >= nextLevel)
            {
                nextLevel = Time.unscaledTime + 0.05f;
                float level = preview ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7f)) * (0.35f + 0.6f * Mathf.PerlinNoise(Time.unscaledTime * 3f, 0f)) : MicCapture.Level;
                levels[levelAt] = Mathf.Clamp01(Mathf.Sqrt(level) * 1.4f);
                levelAt = (levelAt + 1) % Bars;
            }
            Showing = phase != Phase.Hidden;
        }

        // ---- drawing ----

        void Panel(Rect r, Color color, float lean = Lean)
        {
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -lean;
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(r.center.x, r.center.y, 0f)) * shear * Matrix4x4.Translate(new Vector3(-r.center.x, -r.center.y, 0f));
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        void Quad(Vector2 c, float w, float h, float angle, Color color)
        {
            var m = GUI.matrix;
            GUI.matrix = m * Matrix4x4.Translate(c) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)) * Matrix4x4.Translate(-c);
            GUI.color = color;
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        static Color TextColor(Color c, float alpha)
        {
            var g = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
            return new Color(g.r, g.g, g.b, c.a * alpha);
        }

        void Text(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false, float outline = 0f)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Clip, padding = new RectOffset(), richText = false };
            style.font = font;
            style.wordWrap = wrap;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
            style.alignment = anchor;
            if (outline > 0f)
            {
                style.normal.textColor = TextColor(Ink, 1f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * outline, r.y + Mathf.Sin(a) * outline, r.width, r.height), text, style);
                }
            }
            style.normal.textColor = TextColor(color, 1f);
            GUI.Label(r, text, style);
        }

        void OnGUI()
        {
            GUI.depth = -30;
            if (Event.current.type != EventType.Repaint) return;
            if (Dogpatch.Map.Minimap.FullOpen) return;
            float now = Time.time;
            bool leaving = phase == Phase.Hidden;
            if (leaving && now - outAt > SlideOut) return;
            s = Gameplay.HudLayout.U;

            if (shown == Phase.Listening && !leaving) EdgeTint(now);

            float w = Mathf.Clamp(Screen.width * 0.27f, 170f * s, 236f * s);
            float h = 156f * s;
            float left = Gameplay.HudLayout.Left;
            bool readyShowing = Gameplay.HudLayout.LobbyShowing || (RaceDirector.Instance != null && RaceDirector.Instance.ResultsIn);
            var call = Gameplay.HudLayout.Call(72f * s, 72f * s, readyShowing);
            float y = Mathf.Max(Gameplay.HudLayout.Top, (Gameplay.HudLayout.CallFromLeft ? call.y : Gameplay.HudLayout.Bottom - 72f * s) - 14f * s - h);
            float slide = leaving ? 1f - Mathf.Pow(Mathf.Clamp01((now - outAt) / SlideOut), 2f) : Overshoot((now - inAt) / SlideIn);
            float x = Mathf.LerpUnclamped(-w - 40f * s, left, slide);
            float since = now - phaseAt;
            if (shown == Phase.Decline && since < 0.4f) x += Mathf.Sin(since * 60f) * 6f * s * (1f - since / 0.4f);
            var card = new Rect(x, y, w, h);

            Panel(new Rect(card.x - 3f * s, card.y - 3f * s, card.width + 6f * s, card.height + 6f * s), Pink);
            Panel(card, Ink);
            var head = new Rect(card.x, card.y, card.width, 30f * s);
            Panel(head, Pink);
            var avatar = new Rect(head.x + 10f * s, head.y + 3f * s, 24f * s, 24f * s);
            Panel(avatar, Paper);
            Jay(avatar);
            Text(new Rect(avatar.xMax + 8f * s, head.y, head.width - 50f * s, head.height), "JAY", 15f, Color.white);
            float liveDot = 0.5f + 0.5f * Mathf.Sin(now * 6f);
            Panel(new Rect(head.xMax - 22f * s, head.center.y - 4f * s, 8f * s, 8f * s), A(Color.white, 0.5f + 0.5f * liveDot), 0f);

            var body = new Rect(card.x + 12f * s, head.yMax + 8f * s, card.width - 24f * s, card.height - head.height - 16f * s);
            switch (shown)
            {
                case Phase.Listening: Listening(body, now, since); break;
                case Phase.Thinking: Thinking(body, now); break;
                case Phase.Reply: Reply(body, since, false); break;
                case Phase.Decline: Reply(body, since, true); break;
            }
        }

        // Jay, your buddy on the line: a flat low-poly face with a backwards cap and a headset mic.
        void Jay(Rect r)
        {
            float u = r.width / 24f;
            Rect At(float x, float y, float w, float h) => new Rect(r.x + x * u, r.y + y * u, w * u, h * u);
            Panel(At(5f, 6f, 14f, 15f), Skin, 0f);
            Panel(At(4f, 3f, 16f, 5f), Ink, 0f);
            Panel(At(15f, 6f, 6f, 2f), Ink, 0f);
            Panel(At(8f, 11f, 2f, 2f), Ink, 0f);
            Panel(At(14f, 11f, 2f, 2f), Ink, 0f);
            Panel(At(9f, 16f, 6f, 1.5f), Ink, 0f);
            Panel(At(3f, 10f, 2f, 6f), Pink, 0f);
            Panel(At(4f, 16f, 6f, 1.5f), Pink, 0f);
        }

        void EdgeTint(float now)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4f);
            float e = 10f * s;
            for (int i = 0; i < 2; i++)
            {
                float t = e * (i == 0 ? 1f : 2.4f);
                var c = A(Pink, (i == 0 ? 0.22f : 0.06f) * (0.6f + 0.4f * pulse));
                GUI.color = c;
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, Screen.height - t, Screen.width, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, t, t, Screen.height - 2f * t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width - t, t, t, Screen.height - 2f * t), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        void Listening(Rect body, float now, float since)
        {
            bool blocked = !preview && calls != null && calls.Voice != null && calls.Voice.MicBlocked;
            Text(new Rect(body.x, body.y, body.width, 26f * s), blocked ? "MIC OFF" : "LISTENING…", 24f, blocked ? Pink : Sun, TextAnchor.MiddleLeft, false, 1.6f * s);

            var mic = new Vector2(body.x + 26f * s, body.y + 62f * s);
            float drain = Mathf.Clamp01(1f - since / ListenWindow);
            int segs = 28;
            for (int i = 0; i < segs; i++)
            {
                float a = -90f + i * 360f / segs;
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                bool lit = (float)i / segs < drain;
                Quad(mic + dir * 24f * s, 6f * s, 3.4f * s, a + 90f, lit ? Pink : A(Paper, 0.18f));
            }
            float pulse = 1f + 0.12f * Mathf.Sin(now * 9f) + 0.25f * levels[(levelAt + Bars - 1) % Bars];
            Panel(new Rect(mic.x - 15f * s * pulse, mic.y - 15f * s * pulse, 30f * s * pulse, 30f * s * pulse), Pink, 0f);
            Panel(new Rect(mic.x - 4.5f * s, mic.y - 10f * s, 9f * s, 13f * s), Color.white, 0f);
            Panel(new Rect(mic.x - 7f * s, mic.y + 4f * s, 14f * s, 2.4f * s), Color.white, 0f);
            Panel(new Rect(mic.x - 1.2f * s, mic.y + 4f * s, 2.4f * s, 6f * s), Color.white, 0f);

            float wx = mic.x + 34f * s;
            float ww = body.xMax - wx;
            float bw = ww / Bars;
            for (int i = 0; i < Bars; i++)
            {
                float v = levels[(levelAt + i) % Bars];
                float bh = Mathf.Max(3f * s, v * 34f * s);
                Panel(new Rect(wx + i * bw, mic.y - bh * 0.5f, bw * 0.6f, bh), i == Bars - 1 ? Sun : Color.white, 0.1f);
            }

            var words = string.IsNullOrEmpty(transcript) ? "Say what you want…" : "“" + transcript + "”";
            Text(new Rect(body.x, body.y + 92f * s, body.width, 26f * s), words, 12.5f, string.IsNullOrEmpty(transcript) ? A(Paper, 0.55f) : Color.white, TextAnchor.UpperLeft, true);
        }

        void Thinking(Rect body, float now)
        {
            Text(new Rect(body.x, body.y, body.width, 26f * s), "THINKING", 24f, Color.white, TextAnchor.MiddleLeft, false, 1.6f * s);
            float tw = style.CalcSize(Dogpatch.Gameplay.HudLayout.Text("THINKING")).x;
            for (int i = 0; i < 3; i++)
            {
                float hop = Mathf.Max(0f, Mathf.Sin(now * 8f - i * 0.9f));
                Panel(new Rect(body.x + tw + 6f * s + i * 11f * s, body.y + 16f * s - hop * 7f * s, 7f * s, 7f * s), Sun, 0f);
            }
            var words = string.IsNullOrEmpty(transcript) ? "…" : "“" + transcript + "”";
            Text(new Rect(body.x, body.y + 36f * s, body.width, 70f * s), words, 15f, Paper, TextAnchor.UpperLeft, true);
        }

        void Reply(Rect body, float since, bool declined)
        {
            float pop = Overshoot(since / 0.3f);
            string title = declined ? "NOPE!" : "DONE!";
            var c = new Vector2(body.x, body.y + 13f * s);
            float size = 26f * Mathf.Max(0.05f, pop);
            Text(new Rect(c.x, c.y - 16f * s * pop, body.width, 32f * s * pop), title, size, declined ? Pink : Sun, TextAnchor.MiddleLeft, false, 1.8f * s);
            if (!declined && since < 0.35f)
            {
                float p = since / 0.35f;
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 36f;
                    var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                    Quad(new Vector2(body.x + 40f * s, c.y) + dir * Mathf.Lerp(14f, 70f, p) * s, 14f * s * (1f - p), 4f * s, a, A(Sun, 1f - p));
                }
            }
            var words = string.IsNullOrEmpty(line) ? (declined ? CallController.NoAsksLine : "") : line;
            Text(new Rect(body.x, body.y + 36f * s, body.width, 72f * s), words, 15f, Color.white, TextAnchor.UpperLeft, true);
        }

        // ---- headless stills ----

        IEnumerator Capture(string folder)
        {
            Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            while (RaceDirector.Instance == null || !RaceDirector.Instance.Setup) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            preview = true;
            var shots = new (string name, Phase phase, string transcript, string line, float hold)[]
            {
                ("1-listening", Phase.Listening, "make it night", "", 1.6f),
                ("2-thinking", Phase.Thinking, "make it night and make everyone tiny", "", 0.8f),
                ("3-reply", Phase.Reply, "", "Lights out, Dogpatch. Good luck.", 0.18f),
                ("4-decline", Phase.Decline, "", "Ha, nice try, man. One per phone.", 0.5f),
            };
            foreach (var shot in shots)
            {
                previewPhase = shot.phase;
                transcript = shot.transcript;
                line = shot.line;
                yield return new WaitForSecondsRealtime(shot.hold);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, shot.name + ".png"));
                Debug.Log("[CallCard] captured " + shot.name);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            previewPhase = Phase.Hidden;
            yield return new WaitForSecondsRealtime(0.1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "5-slide-out.png"));
            yield return new WaitForSecondsRealtime(0.6f);
            Debug.Log("[CallCard] capture done");
            Application.Quit();
        }
    }
}
