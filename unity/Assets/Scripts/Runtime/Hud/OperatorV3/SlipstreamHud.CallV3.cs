using Dogpatch.Operator;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // HUD v3 board 5, the Operator call at your thumb: a pink handset plate with one sunshine pip per
    // ask left; while a call is open it grows into the video-call phone (SlipstreamHud.VideoCallV3) and
    // folds back when the call ends. A pink edge tint pulses while the mic is open. Input stays in
    // SlipstreamHud (callRect).
    public sealed partial class SlipstreamHud
    {
        // Headless stills (-call-v3-capture): forces a call state, the words heard and the asks left.
        public static CallState? PreviewState;
        public static string PreviewWords = "";
        public static int PreviewAsks = -1;

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

            float h = 64f * s;
            float w = talking && !micOff ? 196f * s : thinking ? 140f * s : 96f * s;
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

            if (micOff)
            {
                V3.HudPlates.Edged(r, White, Pink, 3f * s);
                Handset(r.center, 0.65f * h, Pink);
                var tagSize = Measure("MIC OFF", 13f);
                var tag = new Rect(r.x + 8f * s, r.y - 30f * s, tagSize.x + 40f * s, 24f * s);
                Panel(tag, Ink);
                V3.HudIcons.Draw("talk", new Rect(tag.x + 6f * s, tag.y + 2f * s, 20f * s, 20f * s), Pink);
                Text(new Rect(tag.x + 30f * s, tag.y, tag.width, tag.height), "MIC OFF", 13f, White);
                return;
            }

            if (talking && PreviewState == null && now - calls.StateSince < 0.12f)
            {
                V3.HudPlates.Call(new Rect(r.x, r.y, 96f * s, h), V3.CallLook.Pressed, -1, s);
                return;
            }

            if (talking || thinking || state == CallState.Answered)
            {
                callRect = VideoCall(Gameplay.HudLayout.Call(96f * s, h, readyShowing), state, readyShowing, now);
                return;
            }

            bool fresh = calls.Calls == 0 && !talking && !thinking;
            if (fresh && buzz >= 1.2f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * (buzz < 1.2f ? 18f : 4f));
                DrawFrame(Inflate(new Rect(r.x - 7f * s, r.y + 6f * s, r.width, r.height), pulse * 3f * s), new Color(Pink.r, Pink.g, Pink.b, 0.35f + 0.5f * pulse), 2f * s);
            }

            VideoShrink(r);

            if (counts && asks == 0 && !talking && !thinking)
            {
                OutOfAsks(r);
                return;
            }

            if (buzz < 1.2f && !thinking)
            {
                float p = Mathf.Repeat(buzz, 0.6f) / 0.6f;
                V3.HudPlates.Frame(V3.HudPlates.Grow(r, p * 8f * s), new Color(Pink.r, Pink.g, Pink.b, 1f - p), 2.5f * s);
            }
            V3.HudPlates.Plate(new Rect(r.x + 4f * s, r.y + 4f * s, r.width, r.height), Ink);
            V3.HudPlates.Plate(r, Pink);
            Handset(new Vector2(thinking ? r.x + 34f * s : r.center.x, r.center.y), 0.65f * h, White);
            if (thinking)
            {
                for (int i = 0; i < 3; i++)
                {
                    float hop = Mathf.Max(0f, Mathf.Sin(now * 9f - i * 0.9f));
                    Panel(new Rect(r.x + 70f * s + i * 18f * s, r.center.y - 2f * s - hop * 9f * s, 12f * s, 9f * s), White);
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
            var tile = new Rect(r.x, r.yMax - 52f * s, 64f * s, 52f * s);
            V3.HudPlates.Call(tile, V3.CallLook.OutOfAsks, -1, s);
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

        // The HudIcons handset at a visible height (the glyph spans about 72% of its square).
        void Handset(Vector2 c, float height, Color color)
        {
            float side = height / 0.72f;
            V3.HudIcons.Draw("call", new Rect(c.x - side * 0.5f, c.y - side * 0.5f, side, side), color);
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

        void EdgeTint(float now)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4f);
            float e = 12f * s;
            var c = new Color(Pink.r, Pink.g, Pink.b, 0.5f + 0.25f * pulse);
            Panel(new Rect(0f, 0f, Screen.width, e), c, 0f);
            Panel(new Rect(0f, Screen.height - e, Screen.width, e), c, 0f);
            Panel(new Rect(0f, e, e, Screen.height - 2f * e), c, 0f);
            Panel(new Rect(Screen.width - e, e, e, Screen.height - 2f * e), c, 0f);
        }
    }
}
