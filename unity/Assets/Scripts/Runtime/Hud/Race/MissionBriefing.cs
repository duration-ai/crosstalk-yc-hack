using System.Collections.Generic;
using Dogpatch.Audio;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The mission briefing (CRO-68). Entering a mission's lobby slides in a card with the route, a
    // one-line objective, the story, and the route length and finish, while the Operator tells the
    // story once per lobby (keyed by room and route, so stepping out of the ring and back does not
    // replay it), with every other bus ducked to about a quarter; opening the mic stops him. From the lock to the results the card collapses
    // to a one-line objective chip. Read-only over RaceDirector: it never changes the lobby or race.
    public sealed class MissionBriefing : MonoBehaviour
    {
        sealed class Brief
        {
            public string Objective;
            public string Story;
            public string Clip;
        }

        static readonly Dictionary<string, Brief> Briefs = new Dictionary<string, Brief>
        {
            [""] = new Brief
            {
                Objective = "Get the invite to YC before the deadline",
                Story = "Dude, YC just sent your interview invite, and it expires in three minutes! Caltrain dumped you at 22nd with zero bars. Get the invite to YC before the deadline!",
                Clip = "mission_caltrain",
            },
            ["a"] = new Brief
            {
                Objective = "Pitch the investor at the cranes",
                Story = "Okay, your lead investor is hearing pitches up at the Crane Cove cranes, and he's already bored. Take the 280 deck, it's faster than your burn rate! Reach the cranes before he funds your competitor!",
                Clip = "mission_viaduct",
            },
            ["b"] = new Brief
            {
                Objective = "Plug in at Pier 70 before the demo dies",
                Story = "Oh no, your demo laptop rode Muni all night and it's on one percent! The last free outlet in Dogpatch is in the Pier 70 brick halls. Plug in before your live demo dies!",
                Clip = "mission_iron",
            },
            ["c"] = new Brief
            {
                Objective = "Stop your cofounder at Esprit Park",
                Story = "You won't believe this: your cofounder is quitting to launch an AI for dogs, and he's telling everyone at Esprit Park. Cut under the freeway, it's shadier than your cap table. Stop him before he tweets it!",
                Clip = "mission_deck",
            },
        };

        static readonly Color Ink = SlipstreamHud.Hex("#1C2347");
        static readonly Color Sun = SlipstreamHud.Hex("#FFCB1F");
        static readonly Color Pink = SlipstreamHud.Hex("#FF3E8E");
        static readonly Color White = SlipstreamHud.Hex("#FFFFFF");
        static readonly Color Paper = SlipstreamHud.Hex("#F7F8F9");
        const float Lean = 0.21f;
        const float StorySize = 14.5f;

        readonly HashSet<string> told = new HashSet<string>();
        AudioSource narration;
        AudioClip narrationClip;
        Font font;
        GUIStyle style;
        float s;
        bool paused;
        bool lobbyWas;
        bool chipWas;
        float lobbyAt = -10f;
        float chipAt = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => new GameObject("MissionBriefing").AddComponent<MissionBriefing>();

        void Awake() => font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");

        void OnEnable() => GameEvents.Paused += OnPaused;

        void OnDisable() => GameEvents.Paused -= OnPaused;

        void OnPaused(bool on) => paused = on;

        // Mirrors SlipstreamHud's lobby panel: the room's lobby or its countdown, before the lock, on
        // foot in or by the start ring.
        static bool InLobby(RaceDirector race)
        {
            if (race == null || !race.Setup || race.Gridded || race.Room == null || race.Game == null || race.Course == null) return false;
            var status = race.Room.status;
            if (status != RoomStatus.Lobby && status != RoomStatus.Countdown) return false;
            var focus = race.Game.Focus;
            if (focus == null || race.Game.CurrentCar != null) return false;
            var d = focus.position - race.Course.RingCenter;
            d.y = 0f;
            return d.magnitude < race.Course.RingRadius * 1.5f;
        }

        static bool Racing(RaceDirector race) => race != null && race.Setup && race.Last != null && (race.Gridded || race.Started) && !race.ResultsIn;

        void Update()
        {
            var race = RaceDirector.Instance;
            bool lobby = InLobby(race);
            if (lobby && !lobbyWas)
            {
                lobbyAt = Time.time;
                if (told.Add(race.RoomId + "|" + race.RouteId) && Briefs.TryGetValue(race.RouteId, out var brief))
                {
                    Debug.Log($"[SpeedDial] mission briefing: {brief.Clip} for room {race.RoomId}");
                    narrationClip = SoundBank.Get(brief.Clip);
                    narration = SoundSystem.PlayVoice(narrationClip);
                }
            }
            // Opening the mic cuts the story short, so the speaker never feeds it into the transcript.
            bool speaking = narration != null && narration.isPlaying && narration.clip == narrationClip;
            if (speaking && race?.Calls != null && race.Calls.State == Dogpatch.Operator.CallState.Talking)
            {
                narration.Stop();
                speaking = false;
            }
            if (!speaking) narration = null;
            SoundSystem.SetDeepDuck(this, speaking);
            lobbyWas = lobby;
            bool chip = Racing(race);
            if (chip && !chipWas) chipAt = Time.time;
            chipWas = chip;
        }

        void OnGUI()
        {
            if (paused || Event.current.type != EventType.Repaint) return;
            var race = RaceDirector.Instance;
            if (race == null || !Briefs.TryGetValue(race.RouteId, out var brief)) return;
            GUI.depth = -4;
            s = Gameplay.HudLayout.U;
            if (lobbyWas && InLobby(race)) Card(race, brief);
            else if (chipWas && Racing(race)) Chip(brief);
        }

        void Card(RaceDirector race, Brief brief)
        {
            var data = race.Course.Data;
            float length = race.Room.routeLength > 0f ? race.Room.routeLength : race.Course.Route != null ? race.Course.Route.Length : 0f;
            string title = string.IsNullOrEmpty(data.routeName) ? "MISSION" : data.routeName.ToUpperInvariant();
            string foot = (length > 0f ? $"{length / 1000f:0.0} KM  ·  " : "") + "FINISH: " + (data.finishName ?? "").ToUpperInvariant();

            float titleH = 26f * s, objH = 22f * s, footH = 18f * s, pad = 9f * s, gap = 4f * s;
            float textW = Gameplay.HudLayout.Briefing(0f).width - 2f * pad - 8f * s;
            // Where the slot is short (4:3 landscape) the story shrinks until the card clears the stick.
            float size = StorySize;
            GUIStyle story;
            float storyH;
            Rect slot;
            while (true)
            {
                story = Style(size, TextAnchor.UpperLeft, true);
                storyH = story.CalcHeight(new GUIContent(brief.Story), textW) + 2f * pad;
                slot = Gameplay.HudLayout.Briefing(titleH + objH + storyH + footH + 3f * gap);
                if (size <= 9f || !OverStick(slot)) break;
                size -= 0.5f;
            }

            var r = Row(0, slot, slot.y, titleH);
            Panel(r, Sun);
            Icon(new Rect(r.x + 10f * s, r.y + 3f * s, 20f * s, 20f * s), "yc", Ink);
            Text(new Rect(r.x + 36f * s, r.y, r.width - 40f * s, r.height), title, 17f, Ink);

            r = Row(1, slot, r.yMax + gap, objH);
            Panel(r, Ink);
            Text(new Rect(r.x + 12f * s, r.y, r.width - 16f * s, r.height), brief.Objective.ToUpperInvariant(), 11.5f, Sun);

            r = Row(2, slot, r.yMax + gap, storyH);
            Panel(r, Paper, 0.06f);
            story = Style(size, TextAnchor.UpperLeft, true);
            story.normal.textColor = TextColor(Ink);
            GUI.Label(new Rect(r.x + pad + 4f * s, r.y + pad, textW, r.height - 2f * pad), brief.Story, story);

            r = Row(3, slot, r.yMax + gap, footH);
            Panel(r, Pink);
            Text(new Rect(r.x + 12f * s, r.y, r.width - 16f * s, r.height), foot, 10f, White);
        }

        static bool OverStick(Rect r)
        {
            var stick = Gameplay.HudLayout.StickScreen;
            float grab = Gameplay.HudLayout.StickRadius * 1.6f;
            return r.xMax > stick.x - grab && r.yMax > Screen.height - stick.y - grab;
        }

        // One card row, sliding in from the right with the HUD's overshoot, staggered like the lobby rows.
        Rect Row(int i, Rect slot, float y, float h)
        {
            float t = Mathf.Clamp01((Time.time - lobbyAt - i * 0.07f) / 0.26f);
            float dx = (1f - Overshoot(t)) * (Screen.width - slot.x + 40f * s);
            var r = new Rect(slot.x + dx, y, slot.width, h);
            if (t < 1f) Stripes(r.xMax + 8f * s, r.y + 3f * s, h - 6f * s, 40f * s, Sun);
            return r;
        }

        void Chip(Brief brief)
        {
            string text = brief.Objective.ToUpperInvariant();
            float h = 24f * s;
            var slot = Gameplay.HudLayout.Briefing(h);
            float w = Mathf.Min(slot.width, Measure(text, 11.5f).x + 46f * s);
            float t = Mathf.Clamp01((Time.time - chipAt) / 0.26f);
            float dx = (1f - Overshoot(t)) * (Screen.width - slot.xMax + w + 40f * s);
            var r = new Rect(slot.xMax - w + dx, slot.y, w, h);
            Panel(r, Ink);
            var flag = new Rect(r.x, r.y, 26f * s, h);
            Panel(flag, Sun);
            Icon(new Rect(flag.x + 4f * s, flag.y + 3f * s, 18f * s, 18f * s), "yc", Ink);
            Text(new Rect(flag.xMax + 8f * s, r.y, r.width - flag.width - 10f * s, h), text, 11.5f, White);
        }

        // ---- drawing, as SlipstreamHud draws ----

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

        void Icon(Rect r, string name, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Pictograms.Get(name), ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        void Stripes(float x, float y, float height, float length, Color color)
        {
            for (int i = 0; i < 3; i++)
                Panel(new Rect(x + i * 4f * s, y + i * height * 0.3f, length * (1f - i * 0.22f), Mathf.Max(2f, height * 0.12f)), color, 0f);
        }

        GUIStyle Style(float size, TextAnchor anchor, bool wrap = false)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Overflow, richText = false, padding = new RectOffset() };
            style.font = font;
            style.wordWrap = wrap;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
            style.alignment = anchor;
            return style;
        }

        Vector2 Measure(string text, float size) => Style(size, TextAnchor.UpperLeft).CalcSize(new GUIContent(text));

        // IMGUI text is linearized once more than textures in a linear-space build.
        static Color TextColor(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;

        // Shrinks the size until the line fits its rect, so long objectives never spill out of a panel.
        void Text(Rect r, string text, float size, Color fill)
        {
            float w = Measure(text, size).x;
            if (w > r.width) size *= r.width / w;
            var st = Style(size, TextAnchor.MiddleLeft);
            st.normal.textColor = TextColor(fill);
            GUI.Label(r, text, st);
        }

        static float Overshoot(float t)
        {
            t -= 1f;
            return 1f + t * t * (3.2f * t + 2.2f);
        }
    }
}
