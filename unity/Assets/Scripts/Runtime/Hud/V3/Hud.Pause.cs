using Dogpatch.Audio;
using Dogpatch.Look;
using Dogpatch.Map;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // HUD v3 pause (handoffs/hud-v3/6-Map-pause-loading.png, screens 4-6). A small pause plate sits
    // in the top-right corner (36x30, 44 hit). Paused shows a PAUSED plate with your racer card on
    // top, five leaning setting bars (sound, music, vibration, controls, graphics) and RESUME under
    // the thumb. Holding the FOUNDER MODE tag for 2 s fills its 12 segments and opens the developer
    // panel: every old debug chip in labelled rows, plus the FPS readout (off by default).
    public sealed partial class Hud
    {
        const float DevHoldSeconds = 2f;
        static readonly Color Pink = SpeedDial.Hex("#FF3E8E");

        bool devOpen;
        float tagHeldSince = -1f;
        bool tagConsumed;
        GUIStyle tiny, body, heading;
        PerfReadout perf;

        static float K => Mathf.Min(Screen.width, Screen.height) / 402f;
        static float GuiTop => Screen.height - HudLayout.Safe.yMax;
        static float GuiBottom => Screen.height - HudLayout.Safe.yMin;

        // The pause plate in GUI space (origin top-left), tucked in the corner clear of the dial.
        static Rect PausePlate
        {
            get
            {
                float u = HudLayout.U;
                return new Rect(HudLayout.Right - 18f * u, GuiTop + 4f * u, 36f * u, 30f * u);
            }
        }

        static bool InPausePlate(Vector2 screen)
        {
            var c = PausePlate.center;
            float half = 22f * HudLayout.U;
            return Mathf.Abs(screen.x - c.x) < half && Mathf.Abs(Screen.height - screen.y - c.y) < half;
        }

        // Hidden from the grid's 3 to GO (HUD v3 race-flow board 4), so the count owns the screen.
        bool PlateShown => !Paused && Game != null && Game.IsReady && !Minimap.FullOpen && !OnGrid;

        static bool OnGrid => Race.RaceDirector.Instance is Race.RaceDirector race && race.Gridded && !race.Started;

        void HandlePausePlate()
        {
            if (!PlateShown) return;
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                var t = UnityEngine.Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && InPausePlate(t.position)) { SetPaused(true); return; }
            }
            if (UnityEngine.Input.touchCount == 0 && UnityEngine.Input.GetMouseButtonDown(0) && InPausePlate(UnityEngine.Input.mousePosition)) SetPaused(true);
        }

        void DrawPausePlateButton()
        {
            if (!PlateShown) return;
            var r = PausePlate;
            float u = HudLayout.U;
            Plate(new Rect(r.x + 1.5f * u, r.y + 2f * u, r.width, r.height), 4f * u, new Color(Ink.r, Ink.g, Ink.b, 0.35f));
            Plate(r, 4f * u, Ink);
            Plate(new Rect(r.center.x - 6f * u, r.y + 8f * u, 4f * u, 14f * u), 1.2f * u, White);
            Plate(new Rect(r.center.x + 2f * u, r.y + 8f * u, 4f * u, 14f * u), 1.2f * u, White);
        }

        static bool Pointer(out Vector2 gui)
        {
            if (UnityEngine.Input.touchCount > 0)
            {
                var p = UnityEngine.Input.GetTouch(0).position;
                gui = new Vector2(p.x, Screen.height - p.y);
                return true;
            }
            if (UnityEngine.Input.GetMouseButton(0))
            {
                var p = (Vector2)UnityEngine.Input.mousePosition;
                gui = new Vector2(p.x, Screen.height - p.y);
                return true;
            }
            gui = default;
            return false;
        }

        // A leaning plate in GUI space; GL draws it, so only on repaint.
        void Plate(Rect r, float lean, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.TRIANGLES);
            Parallelogram(new Vector2(r.center.x, Screen.height - r.center.y), r.width * 0.5f, r.height * 0.5f, lean, color);
            GL.End();
            GL.PopMatrix();
        }

        void PlayGlyph(Vector2 c, float size, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.TRIANGLES);
            var p = new Vector2(c.x, Screen.height - c.y);
            Tri(p + new Vector2(-size * 0.45f, size * 0.55f), p + new Vector2(size * 0.6f, 0f), p + new Vector2(-size * 0.45f, -size * 0.55f), color);
            GL.End();
            GL.PopMatrix();
        }

        void Text(Rect r, string text, GUIStyle style, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            style.fontSize = Mathf.Max(6, size);
            style.alignment = anchor;
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        bool Hit(Rect r) => GUI.Button(r, GUIContent.none, GUIStyle.none);

        bool Option(Rect r, string label, bool selected, float k)
        {
            Plate(r, 4f * k, selected ? Sunshine : new Color(1f, 1f, 1f, 0.08f));
            Text(r, label, tiny, Mathf.RoundToInt(10.5f * k), selected ? Ink : new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
            return Hit(r);
        }

        void DrawPauseMenu()
        {
            tiny ??= new GUIStyle { font = font, clipping = TextClipping.Overflow };
            body ??= new GUIStyle { font = font, clipping = TextClipping.Overflow };
            heading ??= new GUIStyle { font = font, clipping = TextClipping.Overflow };
            float k = K;
            bool landscape = Screen.width > Screen.height;
            var safe = HudLayout.Safe;
            float x0 = safe.xMin + 20f * k, top = GuiTop + 14f * k;

            if (Event.current.type == EventType.Repaint)
            {
                Plate(new Rect(-40f, -10f, Screen.width + 80f, Screen.height + 20f), 0f, new Color(Ink.r, Ink.g, Ink.b, 0.55f));
                Plate(new Rect(0f, 0f, Screen.width * (landscape ? 0.62f : 1f), Screen.height), landscape ? 60f * k : 0f, new Color(Ink.r, Ink.g, Ink.b, 0.35f));
            }

            DrawFounderTag(new Rect(x0, top, 104f * k, 20f * k), k);
            Outlined(new Rect(x0, top + 30f * k, 300f * k, 52f * k), devOpen ? "DEV" : "PAUSED", heading.Size(Mathf.RoundToInt(44f * k)), White, k * 0.9f);

            float colW = 330f * k;
            float bodyTop = top + 96f * k;
            if (devOpen)
            {
                var close = new Rect(landscape ? Screen.width * 0.62f - 60f * k : safe.xMax - 56f * k, top + 36f * k, 36f * k, 36f * k);
                Plate(close, 3f * k, Ink);
                Text(close, "X", body, Mathf.RoundToInt(20f * k), White, TextAnchor.MiddleCenter);
                if (Hit(close)) devOpen = false;
                if (landscape) DrawDevPanel(new Vector2(Screen.width * 0.3f, top), new Vector2(Screen.width * 0.66f, top), Screen.width * 0.3f, k);
                else DrawDevPanel(new Vector2(x0, bodyTop), new Vector2(x0, bodyTop + 5 * 50f * k), colW, k);
            }
            else
            {
                DrawRacerCard(new Rect(x0, bodyTop, colW, 60f * k), k);
                var rows = landscape ? new Vector2(Screen.width * 0.5f, top + 10f * k) : new Vector2(x0, bodyTop + 84f * k);
                DrawSettings(rows, landscape ? Mathf.Min(colW, safe.xMax - 20f * k - rows.x) : colW, k);
            }

            float resumeW = 260f * k, resumeH = 52f * k;
            var resume = new Rect(x0 + 20f * k, GuiBottom - resumeH - (landscape ? 18f : 60f) * k, resumeW, resumeH);
            for (int i = 0; i < 3; i++)
                Plate(new Rect(resume.x - (26f + i * 4f) * k, resume.y + (14f + i * 9f) * k, (22f - i * 5f) * k, 4f * k), 1.5f * k, Sunshine);
            Plate(new Rect(resume.x + 3f * k, resume.y + 4f * k, resumeW, resumeH), 10f * k, new Color(0f, 0f, 0f, 0.25f));
            Plate(resume, 10f * k, Sunshine);
            PlayGlyph(new Vector2(resume.x + 46f * k, resume.center.y), 22f * k, Ink);
            Text(new Rect(resume.x + 70f * k, resume.y, resumeW - 70f * k, resumeH), "RESUME", heading, Mathf.RoundToInt(28f * k), Ink);
            if (Hit(resume)) SetPaused(false);
        }

        void DrawFounderTag(Rect r, float k)
        {
            Plate(r, 4f * k, Sunshine);
            Text(r, "FOUNDER MODE", tiny, Mathf.RoundToInt(10f * k), Ink, TextAnchor.MiddleCenter);
            Text(new Rect(r.xMax + 10f * k, r.y, 90f * k, r.height), Application.version, tiny, Mathf.RoundToInt(9f * k), new Color(1f, 1f, 1f, 0.7f));

            float held = 0f;
            if (Event.current.type == EventType.Repaint)
            {
                var grab = new Rect(r.x - 10f * k, r.y - 10f * k, r.width + 20f * k, r.height + 20f * k);
                bool down = Pointer(out var p);
                if (!down) tagConsumed = false;
                if (down && grab.Contains(p) && !tagConsumed)
                {
                    if (tagHeldSince < 0f) tagHeldSince = Time.unscaledTime;
                    held = (Time.unscaledTime - tagHeldSince) / DevHoldSeconds;
                    if (held >= 1f)
                    {
                        devOpen = !devOpen;
                        tagConsumed = true;
                        tagHeldSince = -1f;
                        Debug.Log("[Hud] developer panel " + (devOpen ? "open" : "closed"));
                    }
                }
                else tagHeldSince = -1f;
            }
            float seg = (r.width - 11f * 1.5f * k) / 12f;
            for (int i = 0; i < 12; i++)
            {
                bool lit = held * 12f > i;
                Plate(new Rect(r.x + i * (seg + 1.5f * k), r.yMax + 3f * k, seg, 3f * k), 1f * k, lit ? Sunshine : new Color(Sunshine.r, Sunshine.g, Sunshine.b, 0.3f));
            }
        }

        void DrawRacerCard(Rect r, float k)
        {
            var race = Race.RaceDirector.Instance;
            string name = string.IsNullOrWhiteSpace(race?.MyName) ? "RACER" : race.MyName.ToUpperInvariant();
            var slot = Slipstream.SlipstreamHud.SlotColor(race?.Me?.color);
            Plate(new Rect(r.x + 3f * k, r.y + 4f * k, r.width, r.height), 8f * k, new Color(0f, 0f, 0f, 0.25f));
            Plate(r, 8f * k, White);
            Plate(new Rect(r.x - 2f * k, r.y, 10f * k, r.height), 8f * k, slot);
            var badge = new Rect(r.x + 18f * k, r.y + 8f * k, 44f * k, r.height - 16f * k);
            Plate(badge, 4f * k, slot);
            Text(badge, name.Substring(0, 1), heading, Mathf.RoundToInt(24f * k), White, TextAnchor.MiddleCenter);
            Text(new Rect(r.x + 74f * k, r.y + 9f * k, r.width, 12f * k), "YOUR RACER", tiny, Mathf.RoundToInt(8.5f * k), new Color(Ink.r, Ink.g, Ink.b, 0.6f));
            Text(new Rect(r.x + 74f * k, r.y + 20f * k, r.width - 90f * k, 32f * k), name, heading, Mathf.RoundToInt(24f * k), Ink);
        }

        void SettingRow(Rect r, string label, float k)
        {
            Plate(r, 8f * k, new Color(Ink.r, Ink.g, Ink.b, 0.9f));
            Text(new Rect(r.x + 16f * k, r.y, 120f * k, r.height), label, body, Mathf.RoundToInt(12f * k), White);
        }

        float Bars(Rect r, float value, float k)
        {
            const int N = 12;
            float gap = 2.5f * k, w = (r.width - gap * (N - 1)) / N;
            int lit = Mathf.RoundToInt(value * N);
            float result = value;
            for (int i = 0; i < N; i++)
            {
                var seg = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                Plate(seg, 2f * k, i < lit ? (i >= N - 2 ? Sunshine : White) : new Color(1f, 1f, 1f, 0.2f));
                if (Hit(new Rect(seg.x, seg.y - 8f * k, w + gap, seg.height + 16f * k))) result = i == 0 && lit == 1 ? 0f : (i + 1f) / N;
            }
            return result;
        }

        void DrawSettings(Vector2 at, float width, float k)
        {
            float h = 36f * k, step = 46f * k;
            string[] labels = { "SOUND", "MUSIC", "VIBRATION", "CONTROLS", "GRAPHICS" };
            for (int i = 0; i < labels.Length; i++)
            {
                var row = new Rect(at.x, at.y + i * step, width, h);
                SettingRow(row, labels[i], k);
                var right = new Rect(row.x + width * 0.45f, row.y + 9f * k, width * 0.5f, h - 18f * k);
                switch (i)
                {
                    case 0:
                    {
                        float v = Bars(right, SoundSettings.Master, k);
                        if (!Mathf.Approximately(v, SoundSettings.Master)) SoundSettings.SetMaster(v);
                        break;
                    }
                    case 1:
                    {
                        float now = SoundSettings.Volume(Bus.Music);
                        float v = Bars(right, now, k);
                        if (!Mathf.Approximately(v, now)) SoundSettings.SetVolume(Bus.Music, v);
                        break;
                    }
                    case 2:
                        Pair(right, "ON", "OFF", SoundSettings.Haptics, on => SoundSettings.SetHaptics(on), k);
                        break;
                    case 3:
                        Pair(right, "STICK", "DRAG", !DragInput.DragAnywhere, stick =>
                        {
                            DragInput.DragAnywhere = !stick;
                            Input?.Cancel();
                            Debug.Log("[Hud] controls " + (stick ? "fixed stick" : "drag anywhere"));
                        }, k);
                        break;
                    case 4:
                    {
                        float[] levels = { EffectsSettings.Low, EffectsSettings.Medium, EffectsSettings.High };
                        string[] names = { "LOW", "MED", "HIGH" };
                        float w = (right.width - 8f * k) / 3f;
                        for (int j = 0; j < 3; j++)
                            if (Option(new Rect(right.x + j * (w + 4f * k), right.y - 2f * k, w, right.height + 4f * k), names[j], Mathf.Approximately(EffectsSettings.Intensity, levels[j]), k))
                                EffectsSettings.Intensity = levels[j];
                        break;
                    }
                }
            }
        }

        void Pair(Rect r, string a, string b, bool isA, System.Action<bool> set, float k)
        {
            float w = (r.width - 4f * k) / 2f;
            if (Option(new Rect(r.x, r.y - 2f * k, w, r.height + 4f * k), a, isA, k) && !isA) set(true);
            if (Option(new Rect(r.x + w + 4f * k, r.y - 2f * k, w, r.height + 4f * k), b, !isA, k) && isA) set(false);
        }

        float DevRow(ref Vector2 at, float width, string label, string[] options, System.Func<int, bool> selected, System.Action<int> pick, float k)
        {
            Text(new Rect(at.x, at.y, width, 14f * k), label, tiny, Mathf.RoundToInt(9.5f * k), Sunshine);
            int perLine = Mathf.Min(options.Length, 5);
            int lines = (options.Length + perLine - 1) / perLine;
            float gap = 4f * k, w = (width - gap * (perLine - 1)) / perLine, h = 24f * k;
            for (int i = 0; i < options.Length; i++)
            {
                var r = new Rect(at.x + (i % perLine) * (w + gap), at.y + 16f * k + (i / perLine) * (h + gap), w, h);
                Plate(r, 4f * k, new Color(Ink.r, Ink.g, Ink.b, 0.85f));
                if (Option(r, options[i], selected(i), k)) pick(i);
            }
            float used = 16f * k + lines * (h + gap) + 6f * k;
            at.y += used;
            return used;
        }

        static readonly string[] TimeNames = { "DAWN", "DAY", "GOLD", "DUSK", "NIGHT" };
        static readonly string[] TimeQueries = { "time=dawn", "time=day", "time=golden", "time=dusk", "time=night" };
        static readonly string[] WeatherNames = { "SUN", "RAIN", "SNOW", "FOG", "CLEAR" };
        static readonly string[] WeatherQueries = { "weather=sun", "weather=rain", "weather=snow", "fog=1", "fog=0.35" };
        static readonly string[] AbcNames = { "A", "B", "C" };
        static readonly string[] OnOff = { "ON", "OFF" };

        void DrawDevPanel(Vector2 first, Vector2 second, float width, float k)
        {
            var at = first;
            DevRow(ref at, width, "TIME", TimeNames, _ => false, i => WorldEnvironment.Apply(TimeQueries[i]), k);
            DevRow(ref at, width, "WEATHER", WeatherNames, _ => false, i => WorldEnvironment.Apply(WeatherQueries[i]), k);
            float[] levels = { EffectsSettings.Low, EffectsSettings.Medium, EffectsSettings.High };
            DevRow(ref at, width, "FX", new[] { "LOW", "MED", "HIGH" }, i => Mathf.Approximately(EffectsSettings.Intensity, levels[i]), i => EffectsSettings.Intensity = levels[i], k);
            DevRow(ref at, width, "MAP STYLE", AbcNames, i => Minimap.MapStyle == i, i => Minimap.MapStyle = i, k);
            var looks = new string[MapLook.All.Length];
            for (int i = 0; i < looks.Length; i++) looks[i] = MapLook.All[i].Name;
            float lookWidth = width;
            if (looks.Length > 3)
            {
                Text(new Rect(at.x, at.y, width, 14f * k), "MAP LOOK", tiny, Mathf.RoundToInt(9.5f * k), Sunshine);
                float gap = 4f * k, w = (width - gap * 2f) / 3f, h = 24f * k;
                for (int i = 0; i < looks.Length; i++)
                {
                    var r = new Rect(at.x + (i % 3) * (w + gap), at.y + 16f * k + (i / 3) * (h + gap), w, h);
                    Plate(r, 4f * k, new Color(Ink.r, Ink.g, Ink.b, 0.85f));
                    if (Option(r, looks[i], MapLook.Index == i, k)) MapLook.Index = i;
                }
                at.y += 16f * k + ((looks.Length + 2) / 3) * (h + gap) + 6f * k;
            }
            else DevRow(ref at, lookWidth, "MAP LOOK", looks, i => MapLook.Index == i, i => MapLook.Index = i, k);

            if (second.y > at.y || second.x != first.x) at = second.x != first.x ? second : at;
            DevRow(ref at, width, "HUD LAYOUT", AbcNames, i => (int)HudLayout.Option == i, i => HudLayout.Option = (HudOption)i, k);
            if (perf == null) perf = FindAnyObjectByType<PerfReadout>();
            DevRow(ref at, width, "FPS READOUT", OnOff, i => (perf != null && perf.On) == (i == 0), i => perf?.Set(i == 0), k);
            DevRow(ref at, width, "SOCKET", new[] { "ONLINE", "OFF" }, i => Race.ConvexRaceSession.SocketEnabled == (i == 0), i => Race.ConvexRaceSession.SocketEnabled = i == 0, k);
            DevRow(ref at, width, "MINIMAP", OnOff, i => Minimap.Shown == (i == 0), i => Minimap.Shown = i == 0, k);
        }
    }

    static class GuiStyleSize
    {
        public static GUIStyle Size(this GUIStyle style, int size)
        {
            style.fontSize = size;
            style.alignment = TextAnchor.MiddleLeft;
            return style;
        }
    }
}
