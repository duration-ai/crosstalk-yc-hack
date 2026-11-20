using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Operator
{
    // The Operator's layer of the Slipstream HUD: what the transcriber hears while Call is held,
    // a countdown chip per active effect, refusal toasts, the confirm sheet for effects that hit
    // everyone, and the transcript of the last call for a moment after the answer. The Call plate
    // (SlipstreamHud) is the one talk button; typed input is a dev-flag text field for live tests.
    // IMGUI, same ink/sun/pink.
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

        void OnEnable() => Gameplay.GameController.Cue += Toast;

        void OnDisable() => Gameplay.GameController.Cue -= Toast;

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

        float Width(string text, float size) => Style(size, TextAnchor.MiddleLeft, Color.white).CalcSize(Dogpatch.Gameplay.HudLayout.Text(text)).x;

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
            s = Gameplay.HudLayout.U;
            live.Clear();
            if (Dogpatch.Map.Minimap.FullOpen) return;

            var chips = Gameplay.HudLayout.Chips;
            Chips(chips.x, chips.y);
            Toasting();
            ConfirmSheet();
            if (DebugInput && Debug.isDebugBuild && !Slipstream.SlipstreamHud.ManualCallsLocked) DebugField(Gameplay.HudLayout.Left, Gameplay.HudLayout.Call(72f * s, 72f * s).y - 2f * s);
        }

        // Effect chips (board 5): a coloured block with the effect's pictogram (pink for yours, the
        // requester's slot colour for theirs, sunshine when they're not in the room) on an ink bar with the label and the seconds left; the last
        // 5 s blink pink. Three at most, then +N.
        void Chips(float left, float y)
        {
            if (Effects == null) return;
            var list = Effects.Chips;
            int shown = 0;
            foreach (var chip in list)
            {
                if (shown == 3)
                {
                    Text(new Rect(left + 6f * s, y, 60f * s, 22f * s), "+" + (list.Count - 3), 14f, Color.white);
                    break;
                }
                shown++;
                var row = chip.Row;
                string label = (string.IsNullOrEmpty(row.label) ? row.effect.Replace('_', ' ') : row.label).ToUpperInvariant();
                float secs = Effects.SecondsLeft(row);
                string time = row.instant ? "!" : Mathf.CeilToInt(secs) + "s";
                bool mine = row.requestedBy == Calls.PlayerId();
                bool ending = !row.instant && secs <= 5f && Mathf.Repeat(Time.time, 0.5f) < 0.25f;
                var theirs = mine ? Pink : Their(row.requestedBy);
                var color = ending ? Pink : theirs;
                var block = new Rect(left, y, 30f * s, 22f * s);
                float w = Width(label, 12f) + Width(time, 13f) + 30f * s;
                var bar = new Rect(block.xMax + 2f * s, y, w, 22f * s);
                Panel(bar, Ink);
                Panel(block, color);
                Slipstream.V3.HudIcons.Draw(Picto(row.effect), new Rect(block.x + 5f * s, block.y + 1f * s, 20f * s, 20f * s), color == Sun ? Ink : Color.white);
                Text(new Rect(bar.x + 9f * s, bar.y, bar.width, bar.height), label, 12f, Color.white);
                Text(new Rect(bar.x, bar.y, bar.width - 8f * s, bar.height), time, 13f, color, TextAnchor.MiddleRight);
                if (!row.instant && row.durationS > 0f)
                    Panel(new Rect(bar.x, bar.yMax - 2.5f * s, bar.width * Mathf.Clamp01(secs / row.durationS), 2.5f * s), color, 0f);
                y += 27f * s;
            }
        }

        static Color Their(string playerId)
        {
            var racer = Race.RaceDirector.Instance?.Last?.racers?.Find(r => r.playerId == playerId);
            return racer != null ? Slipstream.SlipstreamHud.SlotColor(racer.color) : Sun;
        }

        static string Picto(string effect)
        {
            switch (effect)
            {
                case "night": return "night";
                case "sunset": case "day": return "time";
                case "smaller": case "shrink": return "tiny";
                case "bigger": case "grow": case "big_heads": return "giant";
                case "gravity_lower": case "gravity_higher": case "slow_fall": return "gravity";
                case "vehicle_hop": case "launch": case "jump_higher": return "jump";
                case "change_vehicle": case "waymo_swarm": return "swap-ride";
                case "grip_down": case "slippery_ground": return "ice";
                case "storm": case "rain": return "rain";
                case "snow": return "snow";
                case "fog": return "fog";
                case "recolor": return "recolor";
                case "vehicle_faster": case "nitro": case "run_faster": return "boost";
                case "explode": case "demolish": case "set_fire": return "explosion";
                case "stop": case "stop_all": return "stop-all-cars";
                default: return "call";
            }
        }

        void Toasting()
        {
            float t = Time.time - toastAt;
            if (t > 2.8f || string.IsNullOrEmpty(toast)) return;
            float a = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((2.8f - t) / 0.3f);
            float w = Width(toast, 20f) + 44f * s;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Gameplay.HudLayout.Band, w, 40f * s);
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
            string room = Room();
            if (!string.IsNullOrEmpty(room)) Text(new Rect(left, r.y - 20f * s, 260f * s, 18f * s), room, 11f, Color.white);
        }
    }
}
