using Dogpatch.Nav;
using UnityEngine;
using static Dogpatch.HudV3.V3Draw;

namespace Dogpatch.HudV3
{
    // HUD v3 board 5, the screen half of the collar: a small sunshine pin standing at the objective
    // far up the road with the metres left (hidden under 40 m), and a 24x36 pt chevron at the screen
    // edge, 14 pt in, when the way on is more than 70 degrees off the view.
    public sealed class WaypointMarks : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => new GameObject("WaypointMarks").AddComponent<WaypointMarks>();

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Map.Minimap.FullOpen) return;
            var nav = NavService.Instance;
            if (nav == null || !nav.HasObjective || nav.Arrived || nav.Game == null) return;
            var cam = nav.Game.View;
            var focus = nav.Game.Focus;
            if (cam == null || focus == null) return;
            GUI.depth = -3;
            var color = nav.Kind == NavKind.Ask ? Pink : Sun;
            float p = P;

            var forward = cam.transform.forward;
            var flat = new Vector2(forward.x, forward.z);
            float off = Vector2.SignedAngle(nav.Heading, flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector2.up);
            if (Mathf.Abs(off) > 70f)
            {
                bool right = off > 0f;
                float w = 24f * p, h = 36f * p, inset = 14f * p;
                var safe = Gameplay.HudLayout.Safe;
                float x = right ? safe.xMax - inset - w : safe.xMin + inset;
                float y = Screen.height * 0.5f - h * 0.5f;
                Chevron(new Rect(x, y, w, h), right, color);
            }

            if (nav.Remaining < 40f) return;
            var sp = cam.WorldToScreenPoint(nav.Objective + Vector3.up * 2f);
            if (sp.z < 1f || sp.x < 0f || sp.x > Screen.width || sp.y < 0f || sp.y > Screen.height) return;
            float gy = Screen.height - sp.y;
            var pin = new Rect(sp.x - 9f * p, gy - 26f * p, 18f * p, 18f * p);
            Diamond(new Vector2(sp.x, pin.yMax), 9f * p + 2f * p, Ink);
            Panel(new Rect(pin.x - 2f * p, pin.y - 2f * p, pin.width + 4f * p, pin.height + 4f * p), Ink, 0f);
            Diamond(new Vector2(sp.x, pin.yMax), 9f * p, color);
            Panel(pin, color, 0f);
            var m = nav.Remaining >= 1000f ? $"{nav.Remaining / 1000f:F1}km" : $"{Mathf.Round(nav.Remaining / 10f) * 10f:F0}m";
            var size = Measure(m, 14f);
            Text(new Rect(sp.x - size.x * 0.5f, pin.y - size.y - 2f * p, size.x, size.y), m, 14f, White, TextAnchor.UpperLeft, 1.4f);
        }

        // A square turned 45 degrees: the pin's point, so the plate reads as a pentagon.
        static void Diamond(Vector2 c, float half, Color color)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, c);
            float side = half * 1.414f;
            GUI.color = color;
            GUI.DrawTexture(new Rect(c.x - side * 0.5f, c.y - side * 0.5f, side, side), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        // Two arms leaning toward the side the way on lies, ink-rimmed.
        static void Chevron(Rect r, bool right, Color color)
        {
            var m = GUI.matrix;
            var c = r.center;
            float len = r.height * 0.62f, thick = r.width * 0.4f;
            for (int pass = 0; pass < 2; pass++)
            {
                float grow = pass == 0 ? 2.5f * P : 0f;
                GUI.color = pass == 0 ? Ink : color;
                foreach (float sign in new[] { -1f, 1f })
                {
                    var center = new Vector2(c.x, c.y + sign * len * 0.36f);
                    GUIUtility.RotateAroundPivot(sign * (right ? 35f : -35f), center);
                    GUI.DrawTexture(new Rect(center.x - thick * 0.5f - grow, center.y - len * 0.5f - grow, thick + 2f * grow, len + 2f * grow), Texture2D.whiteTexture);
                    GUI.matrix = m;
                }
            }
            GUI.color = Color.white;
        }
    }
}
