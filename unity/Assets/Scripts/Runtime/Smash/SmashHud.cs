using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Smash
{
    // Points pops and the smash counter, in Slipstream's type: Exo 2 Black Italic, sunshine with a
    // thin ink outline, no frames. A pop appears at the hit in the world, kicks up, then flies into
    // the small counter in the top-left corner under the race place, which bumps and tallies. The
    // middle of the screen stays clear.
    public sealed class SmashHud : MonoBehaviour
    {
        static readonly Color Ink = new Color32(0x1C, 0x23, 0x47, 0xFF);
        static readonly Color Sunshine = new Color32(0xFF, 0xCB, 0x1F, 0xFF);
        const float Rise = 0.55f;
        const float Fly = 0.4f;

        public Camera View;
        public Func<CarController> Focus;

        struct Pop
        {
            public Vector3 World;
            public string Text;
            public int Points;
            public float Born;
            public Vector2 Screen;
        }

        readonly List<Pop> pops = new List<Pop>();
        GUIStyle style;
        Font font;
        int shown;
        int target;
        float bump;
        CarController focused;

        void OnEnable() => SmashScore.Scored += OnScored;

        void OnDisable() => SmashScore.Scored -= OnScored;

        void OnScored(SmashScoreEvent e)
        {
            var focus = Focus?.Invoke();
            if (focus == null || e.Scorer != focus) return;
            if (pops.Count >= 10) pops.RemoveAt(0);
            pops.Add(new Pop { World = e.Position, Text = "+" + e.Points + " " + e.Label, Points = e.Points, Born = Time.unscaledTime });
        }

        void Update()
        {
            var focus = Focus?.Invoke();
            if (focus != focused)
            {
                focused = focus;
                target = shown = SmashScore.TotalFor(focus);
            }
            if (shown < target) shown = Mathf.Min(target, shown + Mathf.Max(1, Mathf.CeilToInt((target - shown) * Time.unscaledDeltaTime * 8f)));
            bump = Mathf.Max(0f, bump - Time.unscaledDeltaTime * 3f);
        }

        Vector2 CounterAnchor(float s)
        {
            var safe = Screen.safeArea;
            return new Vector2(safe.xMin + 28f * s, Screen.height - safe.yMax + 170f * s);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = Mathf.Min(Screen.width, Screen.height) / 720f;
            if (style == null)
            {
                font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
                style = new GUIStyle(GUI.skin.label) { font = font, wordWrap = false, alignment = TextAnchor.MiddleLeft, richText = false };
            }
            var anchor = CounterAnchor(s);
            var now = Time.unscaledTime;

            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                float age = now - p.Born;
                if (age > Rise + Fly)
                {
                    target += p.Points;
                    bump = 1f;
                    pops.RemoveAt(i);
                    continue;
                }
                if (age < Rise || p.Screen == Vector2.zero)
                {
                    if (View == null) continue;
                    var sp = View.WorldToScreenPoint(p.World);
                    if (sp.z <= 0f) sp = new Vector3(Screen.width * 0.5f, Screen.height * 0.6f, 1f);
                    p.Screen = new Vector2(sp.x, Screen.height - sp.y);
                    pops[i] = p;
                }
                Vector2 pos;
                float scale;
                if (age < Rise)
                {
                    float t = age / Rise;
                    float ease = 1f - (1f - t) * (1f - t);
                    pos = p.Screen + Vector2.down * 90f * s * ease;
                    scale = t < 0.2f ? Mathf.Lerp(0.5f, 1.2f, t / 0.2f) : Mathf.Lerp(1.2f, 1f, (t - 0.2f) / 0.8f);
                }
                else
                {
                    float t = (age - Rise) / Fly;
                    float ease = t * t;
                    var from = p.Screen + Vector2.down * 90f * s;
                    pos = Vector2.Lerp(from, anchor + new Vector2(60f * s, -10f * s), ease);
                    scale = Mathf.Lerp(1f, 0.45f, ease);
                }
                Outlined(p.Text, pos, Mathf.RoundToInt(40f * s * scale), Sunshine, true, s);
            }

            var focus = focused;
            if (focus == null || (target == 0 && pops.Count == 0)) return;
            int mult = SmashScore.MultiplierFor(focus);
            int size = Mathf.RoundToInt(44f * s * (1f + bump * 0.25f));
            Outlined(shown.ToString("N0"), anchor, size, Color.white, false, s);
            Outlined("SMASH", anchor + new Vector2(4f * s, -38f * s), Mathf.RoundToInt(20f * s), Color.white, false, s);
            if (mult > 1) Outlined("x" + mult, anchor + new Vector2(96f * s, -38f * s), Mathf.RoundToInt(26f * s), Sunshine, false, s);
        }

        void Outlined(string text, Vector2 pos, int size, Color color, bool centered, float s)
        {
            if (size < 6) return;
            style.fontSize = size;
            var content = new GUIContent(text);
            var dim = style.CalcSize(content);
            var r = new Rect(centered ? pos.x - dim.x * 0.5f : pos.x, pos.y - dim.y * 0.5f, dim.x + 4f, dim.y);
            float o = Mathf.Max(1.5f, size * 0.06f);
            style.normal.textColor = Display(Ink);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o, r.width, r.height), content, style);
            }
            style.normal.textColor = Display(color);
            GUI.Label(r, content, style);
        }

        // IMGUI in a linear-space project darkens text colours, so hand it the gamma-encoded colour.
        static Color Display(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
    }
}
