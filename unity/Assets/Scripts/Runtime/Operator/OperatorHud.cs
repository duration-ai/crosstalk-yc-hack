using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Operator
{
    // The Operator's layer of the Slipstream HUD: what the transcriber hears while Call is held,
    // a countdown chip per active effect, refusal toasts, the confirm sheet for effects that hit
    // everyone, and (debug) a text field that skips the microphone plus a TALK/SEND button that
    // speaks without a payphone. IMGUI, same ink/sun/pink.
    public sealed class OperatorHud : MonoBehaviour
    {
        const float ConfirmWindow = 10f;

        public CallController Calls;
        public OperatorEffects Effects;
        public bool DebugInput;
        public Func<string> Room = () => "";
        public Action<ActiveEffect, bool> Confirmed;

        static readonly Color Ink = Slipstream.SlipstreamHud.Hex("#1C2347");
        static readonly Color Sun = Slipstream.SlipstreamHud.Hex("#FFCB1F");
        static readonly Color Pink = Slipstream.SlipstreamHud.Hex("#FF3E8E");
        static readonly Color Paper = Slipstream.SlipstreamHud.Hex("#F7F8F9");
        const float Lean = 0.21f;

        Font font;
        GUIStyle style;
        GUIStyle field;
        GUIStyle invisible;
        float s;
        string toast;
        float toastAt = -10f;
        ActiveEffect pending;
        float pendingAt;
        string typed = "";
        readonly List<Rect> live = new List<Rect>();

        public void Toast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            toast = message;
            toastAt = Time.time;
        }

        public void Confirm(ActiveEffect row)
        {
            pending = row;
            pendingAt = Time.time;
        }

        // True when a screen point (pixels, origin bottom left) lands on one of these controls.
        public bool HitTest(Vector2 screen)
        {
            var p = new Vector2(screen.x, Screen.height - screen.y);
            foreach (var r in live) if (r.Contains(p)) return true;
            return false;
        }

        void Awake() => font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");

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

        GUIStyle Style(float size, TextAnchor anchor, Color color)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset() };
            style.font = font;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
            style.alignment = anchor;
            style.normal.textColor = color;
            return style;
        }

        void Text(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft) => GUI.Label(r, text, Style(size, anchor, color));

        float Width(string text, float size) => Style(size, TextAnchor.MiddleLeft, Color.white).CalcSize(new GUIContent(text)).x;

        bool Button(Rect r, string label, Color fill, Color ink)
        {
            live.Add(r);
            Panel(new Rect(r.x - 2f * s, r.y - 2f * s, r.width + 4f * s, r.height + 4f * s), Ink);
            Panel(r, fill);
            Text(r, label, 16f, ink, TextAnchor.MiddleCenter);
            if (invisible == null) invisible = new GUIStyle();
            return GUI.Button(r, GUIContent.none, invisible);
        }

        void OnGUI()
        {
            if (Calls == null) return;
            GUI.depth = -6;
            s = Mathf.Min(Screen.height / 402f, Screen.width / 560f);
            var safe = Screen.safeArea;
            float left = safe.xMin + 22f * s;
            float right = safe.xMax - 22f * s;
            float top = Screen.height - safe.yMax + 14f * s;
            float bottom = Screen.height - safe.yMin - 20f * s;
            live.Clear();

            Hearing(right, bottom);
            Chips(left, top + 92f * s);
            Toasting();
            ConfirmSheet();
            if (DebugInput) DebugField(left, bottom);
        }

        void Hearing(float right, float bottom)
        {
            string words = Calls.State == CallState.Talking ? Calls.Partial : Calls.State == CallState.Waiting ? (string.IsNullOrEmpty(Calls.Heard) ? Calls.Partial : Calls.Heard) : "";
            if (Calls.State == CallState.Talking && string.IsNullOrEmpty(words)) words = "…";
            if (string.IsNullOrEmpty(words)) return;
            words = "“" + words + "”";
            float w = Mathf.Min(Width(words, 15f) + 28f * s, Screen.width * 0.6f);
            var r = new Rect(right - w, bottom - 150f * s, w, 30f * s);
            Panel(r, new Color(Ink.r, Ink.g, Ink.b, 0.9f));
            Text(new Rect(r.x + 14f * s, r.y, r.width - 20f * s, r.height), words, 15f, Calls.State == CallState.Waiting ? Sun : Color.white);
        }

        void Chips(float left, float y)
        {
            if (Effects == null) return;
            foreach (var chip in Effects.Chips)
            {
                var row = chip.Row;
                string label = (string.IsNullOrEmpty(row.label) ? row.effect.Replace('_', ' ') : row.label).ToUpperInvariant();
                float secs = Effects.SecondsLeft(row);
                string time = row.instant ? "!" : Mathf.CeilToInt(secs) + "s";
                float w = Width(label, 13f) + Width(time, 13f) + 40f * s;
                var block = new Rect(left, y, 30f * s, 22f * s);
                var bar = new Rect(block.xMax + 2f * s, y, w, 22f * s);
                Panel(bar, Paper);
                Panel(block, row.requestedBy == Calls.PlayerId() ? Pink : Sun);
                Text(block, time, 12f, Ink, TextAnchor.MiddleCenter);
                Text(new Rect(bar.x + 10f * s, bar.y, bar.width, bar.height), label, 13f, Ink);
                if (!row.instant && row.durationS > 0f)
                {
                    float k = Mathf.Clamp01(secs / row.durationS);
                    Panel(new Rect(bar.x, bar.yMax - 3f * s, bar.width * k, 3f * s), Pink, 0f);
                }
                y += 27f * s;
            }
        }

        void Toasting()
        {
            float t = Time.time - toastAt;
            if (t > 2.8f || string.IsNullOrEmpty(toast)) return;
            float a = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((2.8f - t) / 0.3f);
            float w = Width(toast, 20f) + 44f * s;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.28f, w, 40f * s);
            Panel(r, new Color(Pink.r, Pink.g, Pink.b, a));
            Text(r, toast, 20f, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter);
        }

        void ConfirmSheet()
        {
            if (pending == null) return;
            float left = ConfirmWindow - (Time.time - pendingAt);
            if (left <= 0f)
            {
                Confirmed?.Invoke(pending, false);
                pending = null;
                return;
            }
            float w = 330f * s;
            float h = 128f * s;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
            live.Add(r);
            Panel(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), Ink);
            Panel(r, Paper);
            string title = string.IsNullOrEmpty(pending.label) ? pending.effect.Replace('_', ' ') : pending.label;
            Text(new Rect(r.x + 18f * s, r.y + 8f * s, r.width - 36f * s, 28f * s), title.ToUpperInvariant() + "?", 20f, Pink);
            var body = Style(14f, TextAnchor.UpperLeft, Ink);
            body.wordWrap = true;
            GUI.Label(new Rect(r.x + 18f * s, r.y + 38f * s, r.width - 36f * s, 40f * s), "This will affect everyone, including you.", body);
            body.wordWrap = false;
            Panel(new Rect(r.x + 18f * s, r.y + 74f * s, (r.width - 36f * s) * (left / ConfirmWindow), 4f * s), Sun, 0f);
            if (Button(new Rect(r.x + 18f * s, r.yMax - 40f * s, 130f * s, 30f * s), "DO IT", Pink, Color.white))
            {
                Confirmed?.Invoke(pending, true);
                pending = null;
            }
            else if (Button(new Rect(r.xMax - 148f * s, r.yMax - 40f * s, 130f * s, 30f * s), "NEVER MIND", Paper, Ink))
            {
                Confirmed?.Invoke(pending, false);
                pending = null;
            }
        }

        void DebugField(float left, float bottom)
        {
            var r = new Rect(left, bottom - 64f * s, 210f * s, 26f * s);
            live.Add(r);
            if (field == null) field = new GUIStyle(GUI.skin.textField);
            field.fontSize = Mathf.RoundToInt(13f * s);
            GUI.SetNextControlName("operator-say");
            typed = GUI.TextField(r, typed, 80, field);
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "operator-say";
            if ((Button(new Rect(r.xMax + 6f * s, r.y, 52f * s, r.height), "SAY", Sun, Ink) || enter) && Calls.Say(typed))
            {
                typed = "";
                GUI.FocusControl(null);
            }
            if (Calls.Voice != null)
            {
                bool listening = Calls.State == CallState.Talking;
                var talk = new Rect(r.xMax + 64f * s, r.y, 64f * s, r.height);
                if (Button(talk, listening ? "SEND" : "TALK", listening ? Pink : Sun, listening ? Color.white : Ink)) Calls.ToggleTalk();
            }
            string room = Room();
            if (!string.IsNullOrEmpty(room)) Text(new Rect(left, r.y - 20f * s, 260f * s, 18f * s), room, 11f, Color.white);
        }
    }
}
