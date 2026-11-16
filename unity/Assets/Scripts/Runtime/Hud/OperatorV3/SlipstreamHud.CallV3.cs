using Dogpatch.Operator;
using Dogpatch.Operator.Voice;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // HUD v3 board 5, the Operator call at your thumb: a pink handset plate with one sunshine pip per
    // ask left; held, it stretches into a live chip (mic, voice bars, 12 talk segments) and your words
    // type out frameless above it; let go and three blocks hop on the plate while she thinks. A pink
    // edge tint pulses while the mic is open. Input stays in SlipstreamHud (callRect).
    public sealed partial class SlipstreamHud
    {
        const int TalkSegments = 12;
        const float TalkWindow = 8f;
        const int VoiceBars = 7;

        // Headless stills (-call-v3-capture): forces a call state, the words heard and the asks left.
        public static CallState? PreviewState;
        public static string PreviewWords = "";
        public static int PreviewAsks = -1;

        readonly float[] voice = new float[VoiceBars];
        int voiceAt;
        float voiceNext;
        float callShownAt = -10f;
        int callFrame = -10;

        void PhoneButton(bool readyShowing)
        {
            var calls = Race.Calls;
            float now = Time.time;
            var state = PreviewState ?? calls.State;
            bool talking = state == CallState.Talking;
            bool thinking = state == CallState.Waiting;
            bool counts = PreviewAsks >= 0 || calls.CountsAsks;
            int asks = PreviewAsks >= 0 ? PreviewAsks : Mathf.Max(0, calls.Asks?.Invoke() ?? 0);
            bool micOff = talking && PreviewState == null && calls.Voice != null && calls.Voice.MicBlocked;

            float h = 54f * s;
            float w = talking && !micOff ? 196f * s : thinking ? 128f * s : 86f * s;
            var slot = Gameplay.HudLayout.Call(w, h, readyShowing);
            if (Time.frameCount - callFrame > 2) callShownAt = now;
            callFrame = Time.frameCount;
            float since = now - callShownAt;
            float slide = Overshoot(Mathf.Clamp01(since / 0.32f));
            float x = Mathf.LerpUnclamped(-w - 60f * s, slot.x, slide);
            float buzz = now - calls.BuzzedAt;
            if (buzz < 1.2f && !talking && !thinking) x += Mathf.Sin(buzz * 42f) * 4f * s * (1f - buzz / 1.2f);
            var r = new Rect(x, slot.y, w, h);
            callRect = r;
            callVisible = true;

            if (talking) EdgeTint(now);
            if (since < 0.4f) Stripes(r.x - 8f * s, r.y + 12f * s, 30f * s, 46f * s * (1f - since / 0.4f), Pink, false);

            bool fresh = calls.Calls == 0 && !talking && !thinking;
            if (fresh || buzz < 1.2f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * (buzz < 1.2f ? 18f : 4f));
                DrawFrame(Inflate(new Rect(r.x - 7f * s, r.y + 6f * s, r.width, r.height), pulse * 3f * s), new Color(Pink.r, Pink.g, Pink.b, 0.35f + 0.5f * pulse), 2f * s);
            }

            if (micOff)
            {
                Framed(r, White, Pink, 3f * s);
                Icon(new Rect(r.center.x - 19f * s, r.center.y - 19f * s, 38f * s, 38f * s), "call", Pink);
                var tagSize = Measure("MIC OFF", 13f);
                var tag = new Rect(r.x + 8f * s, r.y - 30f * s, tagSize.x + 40f * s, 24f * s);
                Panel(tag, Ink);
                Panel(new Rect(tag.x + 8f * s, tag.y + 5f * s, 14f * s, 14f * s), Pink, 0f);
                Text(new Rect(tag.x + 30f * s, tag.y, tag.width, tag.height), "MIC OFF", 13f, White);
                return;
            }

            if (counts && asks == 0 && !talking && !thinking)
            {
                OutOfAsks(r);
                return;
            }

            Panel(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), Ink);
            Panel(r, Pink);

            if (talking)
            {
                Mic(new Vector2(r.x + 30f * s, r.center.y));
                Voice(new Rect(r.x + 56f * s, r.y + 9f * s, r.width - 70f * s, r.height - 18f * s), now);
                float left = PreviewState != null ? 0.7f : Mathf.Clamp01(1f - (now - calls.StateSince) / TalkWindow);
                float segW = (r.width - 6f * s) / TalkSegments;
                for (int i = 0; i < TalkSegments; i++)
                    Panel(new Rect(r.x + 4f * s + i * segW, r.y - 12f * s, segW * 0.72f, 5f * s), (float)i / TalkSegments < left ? White : new Color(1f, 1f, 1f, 0.28f), 0.3f);
                Typing(r, now);
                return;
            }

            Icon(new Rect(r.x + 5f * s, r.center.y - 19f * s, 38f * s, 38f * s), "call", White);
            if (thinking)
            {
                for (int i = 0; i < 3; i++)
                {
                    float hop = Mathf.Max(0f, Mathf.Sin(now * 9f - i * 0.9f));
                    Panel(new Rect(r.x + 56f * s + i * 18f * s, r.center.y - 2f * s - hop * 9f * s, 12f * s, 9f * s), White);
                }
            }
            Pips(r, counts, asks);

            float refused = now - calls.TurnedDownAt;
            if (refused < 2.2f)
            {
                float a = Mathf.Clamp01(refused / 0.1f) * Mathf.Clamp01((2.2f - refused) / 0.3f);
                string line = CallController.NoAsksLine.ToUpperInvariant();
                var m = Measure(line, 14f);
                var bar = new Rect(r.xMax + 10f * s, r.center.y - 15f * s, m.x + 24f * s, 30f * s);
                Panel(bar, new Color(Ink.r, Ink.g, Ink.b, a));
                Text(new Rect(bar.x + 12f * s, bar.y, bar.width, bar.height), line, 14f, White, TextAnchor.MiddleLeft, 0f, a);
            }
        }

        // Out of asks: no grey Call. The slot shrinks to a framed handset tile, plus an arrow to the
        // nearest phone pickup still lit (relative to the camera) and its distance when one exists.
        void OutOfAsks(Rect r)
        {
            var tile = new Rect(r.x, r.yMax - 42f * s, 46f * s, 42f * s);
            Framed(tile, White, Pink, 3f * s);
            Icon(new Rect(tile.center.x - 15f * s, tile.center.y - 15f * s, 30f * s, 30f * s), "call", Pink);
            var cam = Camera.main;
            var player = Race.Game != null && Race.Game.Player != null ? Race.Game.Player.transform : null;
            if (cam == null || player == null) return;
            var spots = AskPickups.Positions;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < spots.Count; i++)
            {
                if (!AskPickups.Available(i)) continue;
                float d = Vector3.Distance(spots[i], player.position);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            var to = spots[best] - player.position;
            var fwd = cam.transform.forward;
            float angle = Vector2.SignedAngle(new Vector2(to.x, to.z), new Vector2(fwd.x, fwd.z));

            var c = new Vector2(tile.xMax + 24f * s, tile.center.y);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, c);
            Panel(new Rect(c.x - 2f * s, c.y - 9f * s, 4f * s, 21f * s), White, 0f);
            var tip = new Vector2(c.x, c.y - 12f * s);
            var turned = GUI.matrix;
            GUIUtility.RotateAroundPivot(-40f, tip);
            Panel(new Rect(tip.x - 2f * s, tip.y, 4f * s, 13f * s), White, 0f);
            GUI.matrix = turned;
            GUIUtility.RotateAroundPivot(40f, tip);
            Panel(new Rect(tip.x - 2f * s, tip.y, 4f * s, 13f * s), White, 0f);
            GUI.matrix = m;
            string dist = Mathf.RoundToInt(bestD) + "m";
            Text(new Rect(c.x + 18f * s, tile.y, 120f * s, tile.height), dist, 26f, White, TextAnchor.MiddleLeft, 1.6f * s);
        }

        void Pips(Rect r, bool counts, int asks)
        {
            if (!counts) return;
            int n = Mathf.Min(asks, 5);
            float pw = 12f * s, ph = 7f * s, gap = 3f * s;
            float x0 = r.xMax - 8f * s - n * (pw + gap);
            for (int i = 0; i < n; i++)
            {
                var pip = new Rect(x0 + i * (pw + gap), r.y - ph - 6f * s, pw, ph);
                Panel(new Rect(pip.x - 1.5f * s, pip.y - 1.5f * s, pip.width + 3f * s, pip.height + 3f * s), Ink);
                Panel(pip, Sun);
            }
        }

        void Mic(Vector2 c)
        {
            Panel(new Rect(c.x - 5f * s, c.y - 13f * s, 10f * s, 16f * s), White, 0f);
            Panel(new Rect(c.x - 9f * s, c.y + 1f * s, 2.6f * s, 6f * s), White, 0f);
            Panel(new Rect(c.x + 6.4f * s, c.y + 1f * s, 2.6f * s, 6f * s), White, 0f);
            Panel(new Rect(c.x - 9f * s, c.y + 6f * s, 18f * s, 2.6f * s), White, 0f);
            Panel(new Rect(c.x - 1.3f * s, c.y + 8f * s, 2.6f * s, 5f * s), White, 0f);
        }

        void Voice(Rect r, float now)
        {
            if (Time.unscaledTime >= voiceNext)
            {
                voiceNext = Time.unscaledTime + 0.06f;
                float level = PreviewState != null ? Mathf.Abs(Mathf.Sin(now * 7f)) * (0.4f + 0.6f * Mathf.PerlinNoise(now * 3f, 0f)) : MicCapture.Level;
                voice[voiceAt] = Mathf.Clamp01(Mathf.Sqrt(level) * 1.4f);
                voiceAt = (voiceAt + 1) % VoiceBars;
            }
            float bw = r.width / VoiceBars;
            for (int i = 0; i < VoiceBars; i++)
            {
                float v = Mathf.Max(voice[(voiceAt + i) % VoiceBars], 0.18f + 0.12f * Mathf.Sin(now * 6f + i));
                float bh = Mathf.Max(5f * s, v * r.height);
                Panel(new Rect(r.x + i * bw, r.center.y - bh * 0.5f, bw * 0.5f, bh), White, 0.12f);
            }
        }

        // Your words, typed out frameless above the thumb: white with an ink outline, a pink cursor.
        void Typing(Rect plate, float now)
        {
            string words = PreviewState != null ? PreviewWords : Race.Calls.Partial;
            bool blink = Mathf.Repeat(now, 0.8f) < 0.55f;
            float maxW = Mathf.Min(Screen.width * 0.62f, 300f * s);
            if (string.IsNullOrEmpty(words))
            {
                if (blink) Panel(new Rect(plate.x + 6f * s, plate.y - 40f * s, 5f * s, 16f * s), Pink, 0.1f);
                return;
            }
            var st = Style(17f, TextAnchor.UpperLeft);
            st.wordWrap = true;
            var content = new GUIContent(words);
            float th = st.CalcHeight(content, maxW);
            var box = new Rect(plate.x + 4f * s, plate.y - 22f * s - th, maxW, th);
            st.normal.textColor = TextColor(Ink, 1f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                GUI.Label(new Rect(box.x + Mathf.Cos(a) * 1.6f * s, box.y + Mathf.Sin(a) * 1.6f * s, box.width, box.height), content, st);
            }
            st.normal.textColor = TextColor(White, 1f);
            GUI.Label(box, content, st);
            var cursor = st.GetCursorPixelPosition(box, content, words.Length);
            st.wordWrap = false;
            if (blink) Panel(new Rect(cursor.x + 3f * s, cursor.y + 2f * s, 5f * s, 16f * s), Pink, 0.1f);
        }

        void EdgeTint(float now)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4f);
            float e = 7f * s;
            var c = new Color(Pink.r, Pink.g, Pink.b, 0.5f + 0.25f * pulse);
            Panel(new Rect(0f, 0f, Screen.width, e), c, 0f);
            Panel(new Rect(0f, Screen.height - e, Screen.width, e), c, 0f);
            Panel(new Rect(0f, e, e, Screen.height - 2f * e), c, 0f);
            Panel(new Rect(Screen.width - e, e, e, Screen.height - 2f * e), c, 0f);
        }
    }
}
