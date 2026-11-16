using System.Collections.Generic;
using Dogpatch.Audio;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The mission briefing (CRO-68). Entering a mission's lobby (where HudV3.ArrivalCard shows the
    // route, objective and racers) the Operator tells the story once per lobby (keyed by room and route, so stepping out of the ring and back does not
    // replay it), with every other bus ducked to about a quarter; opening the mic stops him. From the lock to the results a one-line
    // objective chip shows. Read-only over RaceDirector: it never changes the lobby or race.
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
        static readonly Color White = SlipstreamHud.Hex("#FFFFFF");
        const float Lean = 0.21f;

        readonly HashSet<string> told = new HashSet<string>();
        AudioSource narration;
        AudioClip narrationClip;
        Font font;
        GUIStyle style;
        float s;
        bool paused;
        bool hiddenWas;
        bool lobbyWas;
        bool chipWas;
        float chipAt = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => new GameObject("MissionBriefing").AddComponent<MissionBriefing>();

        void Awake() => font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");

        void OnEnable() => GameEvents.Paused += OnPaused;

        void OnDisable() => GameEvents.Paused -= OnPaused;

        void OnPaused(bool on) => paused = on;

        // Out of the way under the pause menu and the full map; it slides back in when they close.
        bool Hidden => paused || Dogpatch.Map.Minimap.FullOpen || Dogpatch.Gameplay.AvatarCapture.CardOpen;

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
            bool hidden = Hidden;
            if (hiddenWas && !hidden) chipAt = Time.time;
            hiddenWas = hidden;
        }

        void OnGUI()
        {
            if (Hidden || Event.current.type != EventType.Repaint) return;
            var race = RaceDirector.Instance;
            if (race == null || !Briefs.TryGetValue(race.RouteId, out var brief)) return;
            GUI.depth = -4;
            s = Gameplay.HudLayout.U;
            if (chipWas && Racing(race)) Chip(brief);
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
