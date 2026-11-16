using Dogpatch.Operator;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // HUD v3 board 5, the top band: let go and your words shoot up to it beside a pink handset block
    // with three hopping blocks; then DONE! slams in sunshine with a burst, or NOPE! in pink on ink
    // shakes three times, and Jay's line rides the pink bar beside the stamp.
    public sealed partial class SlipstreamHud
    {
        public static bool PreviewRefused;
        public static string PreviewLine = "";
        public static float PreviewAt;

        bool answerHooked;
        bool answerRefused;

        // True while the band belongs to the Operator (her thinking or her answer), so news waits.
        bool OperatorBand()
        {
            var calls = Race.Calls;
            if (!answerHooked)
            {
                answerHooked = true;
                calls.Answered += reply => answerRefused = reply != null && (reply.refusal != null || reply.outcome == "refused" || reply.outcome == "declined");
            }
            bool preview = PreviewState != null;
            var state = PreviewState ?? calls.State;
            float since = Time.time - (preview ? PreviewAt : calls.StateSince);
            if (state == CallState.Waiting)
            {
                Heard(preview ? PreviewWords : string.IsNullOrEmpty(calls.Heard) ? calls.Partial : calls.Heard, since);
                return true;
            }
            if (state == CallState.Answered)
            {
                Verdict(preview ? PreviewRefused : answerRefused, preview ? PreviewLine : calls.Line, since);
                return true;
            }
            return false;
        }

        float BandX => narrow ? left : Mathf.Max(left, Screen.width * 0.5f - 230f * s);

        void Heard(string words, float since)
        {
            float rise = EaseOut(Mathf.Clamp01(since / 0.22f));
            float y = Mathf.Lerp(Gameplay.HudLayout.Call(72f * s, 72f * s).y, band, rise);
            float x = BandX;
            var block = new Rect(x, y, 74f * s, 30f * s);
            var text = string.IsNullOrEmpty(words) ? "…" : words;
            float maxW = Mathf.Max(120f * s, right - block.xMax - 30f * s);
            var bar = new Rect(block.xMax + 2f * s, y, Mathf.Min(Measure(text, 14f).x + 26f * s, maxW), 30f * s);
            Panel(bar, Ink);
            Panel(block, Pink);
            Handset(new Vector2(block.x + 18f * s, block.center.y), 20f * s, White);
            float now = Time.time;
            for (int i = 0; i < 3; i++)
            {
                float hop = Mathf.Max(0f, Mathf.Sin(now * 9f - i * 0.9f));
                Panel(new Rect(block.x + 40f * s + i * 11f * s, block.center.y - 1f * s - hop * 6f * s, 8f * s, 6f * s), White);
            }
            Text(new Rect(bar.x + 12f * s, bar.y, bar.width - 16f * s, bar.height), text, 14f, White, TextAnchor.MiddleLeft, 0f, Mathf.Clamp01((since - 0.1f) / 0.12f));
        }

        void Verdict(bool refused, string line, float since)
        {
            float x = BandX;
            if (refused && since < 0.45f) x += Mathf.Sin(since * 42f) * 6f * s * (1f - since / 0.45f);
            float pop = Overshoot(Mathf.Clamp01(since / 0.2f));
            string word = refused ? "NOPE!" : "DONE!";
            float stampW = Measure(word, 17f).x + 48f * s;
            var stamp = new Rect(x, band, stampW, 34f * s);
            var popped = new Rect(stamp.center.x - stamp.width * 0.5f * pop, stamp.center.y - stamp.height * 0.5f * pop, stamp.width * pop, stamp.height * pop);

            if (!refused && since < 0.4f)
            {
                float p = since / 0.4f;
                var c = stamp.center;
                for (int i = 0; i < 9; i++)
                {
                    float a = -160f + i * 20f;
                    var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                    var m = GUI.matrix;
                    var at = c + dir * Mathf.Lerp(20f, 52f, p) * s;
                    GUIUtility.RotateAroundPivot(a, at);
                    Panel(new Rect(at.x - 9f * s * (1f - p), at.y - 2f * s, 18f * s * (1f - p), 4f * s), Sun, 0f);
                    GUI.matrix = m;
                }
            }

            string say = string.IsNullOrEmpty(line) ? "" : line;
            if (!string.IsNullOrEmpty(say) && since > 0.1f)
            {
                float slide = EaseOut(Mathf.Clamp01((since - 0.1f) / 0.2f));
                float maxW = Mathf.Max(140f * s, right - stamp.xMax - 20f * s);
                float w = Mathf.Min(Measure(say, 15f).x + 28f * s, maxW);
                var bar = new Rect(stamp.xMax + 2f * s, band + 3f * s, w * slide, 28f * s);
                Panel(bar, Pink);
                if (slide > 0.9f) Text(new Rect(bar.x + 12f * s, bar.y, w - 16f * s, bar.height), say, 15f, White);
            }

            Panel(Inflate(popped, 2f * s), refused ? Pink : Ink);
            Panel(popped, refused ? Ink : Sun);
            if (pop < 0.6f) return;
            var mark = new Rect(popped.x + 8f * s, popped.center.y - 11f * s, 22f * s, 22f * s);
            if (refused)
            {
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, mark.center);
                Panel(new Rect(mark.x, mark.center.y - 1.6f * s, mark.width, 3.2f * s), Pink, 0f);
                Panel(new Rect(mark.center.x - 1.6f * s, mark.y, 3.2f * s, mark.height), Pink, 0f);
                GUI.matrix = m;
            }
            else Icon(mark, "ready", Ink);
            Text(new Rect(mark.xMax + 4f * s, popped.y, popped.width, popped.height), word, 17f, refused ? Pink : Ink);
        }
    }
}
