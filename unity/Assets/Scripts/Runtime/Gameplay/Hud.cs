using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Minimal IMGUI overlay: street, mode and speed, the drag stick, a hint and attribution.
    public sealed class Hud : MonoBehaviour
    {
        public GameController Game;
        public DragInput Input;
        public bool Minimal;

        string toast;
        float toastUntil;
        Texture2D ring;
        Texture2D dot;
        Texture2D panel;
        GUIStyle label;
        GUIStyle small;

        public void Toast(string message, float seconds = 2.5f)
        {
            toast = message;
            toastUntil = Time.time + seconds;
        }

        void Awake()
        {
            ring = Circle(128, 0.42f, 0.5f, new Color(1f, 1f, 1f, 0.55f));
            dot = Circle(64, 0f, 0.5f, new Color(1f, 1f, 1f, 0.85f));
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.45f));
            panel.Apply();
        }

        static Texture2D Circle(int size, float inner, float outer, Color color)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x - size / 2f + 0.5f, y - size / 2f + 0.5f).magnitude / size;
                    tex.SetPixel(x, y, d >= inner && d <= outer ? color : Color.clear);
                }
            tex.Apply();
            return tex;
        }

        void OnGUI()
        {
            if (Game == null) return;
            float s = Mathf.Min(Screen.width, Screen.height) / 720f;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
                small = new GUIStyle(GUI.skin.label) { wordWrap = true };
            }
            label.fontSize = Mathf.RoundToInt(30 * s);
            small.fontSize = Mathf.RoundToInt(19 * s);
            label.normal.textColor = Color.white;
            small.normal.textColor = new Color(1f, 1f, 1f, 0.9f);

            var safe = Screen.safeArea;
            float left = safe.xMin + 20 * s;
            float top = Screen.height - safe.yMax + 20 * s;
            float width = Mathf.Min(safe.width - 40 * s, 620 * s);
            if (!Minimal)
            {
                GUI.DrawTexture(new Rect(left - 10 * s, top - 6 * s, width, 104 * s), panel);
                GUI.Label(new Rect(left, top, width, 44 * s), string.IsNullOrEmpty(Game.StreetName) ? "Dogpatch, San Francisco" : Game.StreetName, label);
                GUI.Label(new Rect(left, top + 46 * s, width, 40 * s), Game.StatusLine, small);

                string hint = Game.Driving
                    ? "Drag to steer and drive · release to stop · tap your car to get out"
                    : "Drag anywhere to walk · tap to jump · tap a car to get in";
                GUI.Label(new Rect(left, Screen.height - safe.yMin - 92 * s, width, 60 * s), hint, small);
            }
            GUI.Label(new Rect(left, Screen.height - safe.yMin - 38 * s, safe.width - 40 * s, 30 * s), "© OpenStreetMap contributors · DataSF · polyfork.dev", small);

            if (!Minimal && !string.IsNullOrEmpty(toast) && Time.time < toastUntil)
            {
                var size = label.CalcSize(new GUIContent(toast));
                var r = new Rect((Screen.width - size.x) / 2f - 16 * s, Screen.height * 0.3f, size.x + 32 * s, size.y + 16 * s);
                GUI.DrawTexture(r, panel);
                GUI.Label(new Rect(r.x + 16 * s, r.y + 8 * s, size.x, size.y), toast, label);
            }

            if (Input != null && Input.Dragging)
            {
                float reach = Input.Reach;
                var a = new Vector2(Input.Anchor.x, Screen.height - Input.Anchor.y);
                var c = new Vector2(Input.Current.x, Screen.height - Input.Current.y);
                var offset = Vector2.ClampMagnitude(c - a, reach);
                GUI.DrawTexture(new Rect(a.x - reach, a.y - reach, reach * 2f, reach * 2f), ring);
                float k = 46 * s;
                GUI.DrawTexture(new Rect(a.x + offset.x - k, a.y + offset.y - k, k * 2f, k * 2f), dot);
            }
        }
    }
}
