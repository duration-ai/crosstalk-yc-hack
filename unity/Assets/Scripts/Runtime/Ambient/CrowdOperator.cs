using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Operator;
using UnityEngine;

namespace Dogpatch.Ambient
{
    public enum CrowdMood { Normal, Calm, Angry, Panic }

    // Operator hooks (CROS-39). Crowds and ghost town scale the head count: newcomers pop in
    // around you, leavers vanish in a puff. Moods take over walking: panic runs everyone in
    // zigzags with arms flailing, an angry crowd red in the face mobs the player and stamps,
    // a calm one strolls at half pace, swaying. Themes dress every pedestrian in a costume.
    // OperatorFrame redresses the crowd and draws the mood marks every frame.
    public sealed partial class PedestrianSystem
    {
        public static readonly Knob Crowd = new Knob();
        public static CrowdMood Mood;
        public static float MoodPace = 1f;
        public static float Hyper = 1f;

        static readonly List<(CrowdMood mood, float k)> moods = new List<(CrowdMood, float)>();
        static readonly List<string> themes = new List<string>();
        static float moodK;

        readonly Dictionary<Pedestrian, (int id, CrowdRenderer.Outfit outfit)> dressed = new Dictionary<Pedestrian, (int, CrowdRenderer.Outfit)>();
        int crowdBase = -1;
        int popFrom = int.MaxValue;
        float popUntil;
        GameController game;

        public static void AddMood(CrowdMood mood, float k)
        {
            moods.Add((mood, k));
            Remood();
            Instance?.Rouse();
        }

        public static void RemoveMood(CrowdMood mood, float k)
        {
            moods.Remove((mood, k));
            var before = Mood;
            Remood();
            if (before != Mood) Instance?.Settle();
        }

        static void Remood()
        {
            Mood = CrowdMood.Normal;
            moodK = 0f;
            foreach (var (mood, k) in moods)
                if (mood > Mood || (mood == Mood && k > moodK))
                {
                    Mood = mood;
                    moodK = k;
                }
            MoodPace = Mood == CrowdMood.Calm ? Mathf.Lerp(0.75f, 0.4f, moodK) : Mood == CrowdMood.Angry ? 1.5f : 1f;
        }

        public static void AddTheme(string theme)
        {
            themes.Add(theme);
            Instance?.Confetti(theme);
        }

        public static void RemoveTheme(string theme) => themes.Remove(theme);

        // Up off the benches and out of the queues when a mood lands.
        void Rouse()
        {
            if (Mood == CrowdMood.Normal || Mood == CrowdMood.Calm) return;
            foreach (var ped in active)
            {
                if (ped.State != PedState.Sit && ped.State != PedState.BusWait && ped.State != PedState.Idle && ped.State != PedState.WaitCurb) continue;
                LeaveSpotState(ped);
                ped.Crossing = -1;
                Unregister(ped);
                Enter(ped, PedState.Walk, 0f);
            }
        }

        // Back onto the walk graph once a mood ends.
        void Settle()
        {
            foreach (var ped in active)
            {
                ped.Lean = 0f;
                if (ped.State == PedState.Walk) NewTrip(ped);
            }
        }

        public void Recount()
        {
            if (crowdBase < 0) crowdBase = TargetCount;
            TargetCount = Mathf.Max(0, Mathf.RoundToInt(crowdBase * Crowd.Product));
            filled = false;
            popFrom = nextId;
            popUntil = Time.time + 4f;
            if (active.Count <= TargetCount) return;
            var order = new List<Pedestrian>(active);
            var eye = new Vector2(cameraPos.x, cameraPos.z);
            order.Sort((a, b) => Leaving(a, eye).CompareTo(Leaving(b, eye)));
            for (int i = 0; i < order.Count - TargetCount; i++)
            {
                var ped = order[i];
                WorldEffectsB.Poof(ped.Position + Vector3.up * 0.9f, 1.2f, Smash.SmashFx.SmokeLight, 6);
                Despawn(ped);
            }
        }

        float Leaving(Pedestrian p, Vector2 eye)
        {
            float d = Vector2.Distance(p.Pos, eye);
            return d + (InView(p.Pos, d) ? 0f : 300f);
        }

        Vector2 Victim()
        {
            if (game == null) game = FindAnyObjectByType<GameController>();
            var t = game != null ? game.Focus : null;
            return t != null ? new Vector2(t.position.x, t.position.z) : focus;
        }

        bool MoodWalk(Pedestrian ped, float dt, float now, bool near)
        {
            switch (Mood)
            {
                case CrowdMood.Panic:
                {
                    float a = Mathf.PerlinNoise(now * 0.7f, ped.Id * 0.31f) * Mathf.PI * 4f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var away = ped.Pos - Victim();
                    if (away.sqrMagnitude < 15f * 15f && away.sqrMagnitude > 0.01f) dir = (dir + away.normalized * 1.5f).normalized;
                    if (Graph.InBuilding(ped.Pos + dir * 2f, 0.3f)) dir = new Vector2(-dir.y, dir.x);
                    Steer(ped, ped.Pos + dir * 4f, Mathf.Max(3.4f, ped.Cruise * (2f + moodK)), dt, near);
                    ped.Lean = Mathf.Sin(now * 14f + ped.Id) * 7f;
                    return true;
                }
                case CrowdMood.Angry:
                {
                    var victim = Victim();
                    var to = victim - ped.Pos;
                    if (to.sqrMagnitude > 38f * 38f) return false;
                    float a = ped.Id * 2.399f;
                    var spot = victim + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (1.8f + (ped.Id % 3) * 0.7f);
                    if ((spot - ped.Pos).sqrMagnitude > 1f)
                    {
                        Steer(ped, spot, Mathf.Max(2.8f, ped.Cruise * 2f), dt, near);
                        ped.Lean = 6f;
                    }
                    else
                    {
                        Brake(ped, dt);
                        ped.Heading = Mathf.MoveTowardsAngle(ped.Heading, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg, 360f * dt);
                        ped.Lean = 4f + Mathf.Sin(now * 18f + ped.Id) * 5f;
                    }
                    return true;
                }
                case CrowdMood.Calm:
                    ped.Lean = Mathf.Sin(now * 2.2f + ped.Id) * 4f;
                    return false;
            }
            return false;
        }

        void Confetti(string theme)
        {
            var colors = ThemeColors(theme);
            int n = 0;
            foreach (var ped in active)
            {
                float d = Vector2.Distance(ped.Pos, new Vector2(cameraPos.x, cameraPos.z));
                if (!InView(ped.Pos, d) || d > 60f) continue;
                WorldEffectsB.Poof(ped.Position + Vector3.up * 1f, 1f, ((Color)colors[n % colors.Length]).gamma, 6);
                if (++n >= 24) break;
            }
        }

        // Called every frame by the Operator while any crowd effect is on, and once after the last.
        public void OperatorFrame()
        {
            string theme = themes.Count > 0 ? themes[themes.Count - 1] : null;
            bool dress = theme != null || Mood == CrowdMood.Angry || looks.Count > 0;
            if (!dress && dressed.Count > 0) Undress();
            var sprites = Mood != CrowdMood.Normal ? EffectSprites.Get() : null;
            var eye = new Vector2(cameraPos.x, cameraPos.z);
            float now = Time.time;
            int marks = 0;
            foreach (var ped in active)
            {
                if (now < popUntil && ped.Id >= popFrom)
                {
                    float d = Vector2.Distance(ped.Pos, eye);
                    if (d < 70f && InView(ped.Pos, d)) WorldEffectsB.Poof(ped.Position + Vector3.up * 0.9f, 1.1f, Smash.SmashFx.SmokeLight, 6);
                }
                if (dress)
                {
                    if (!dressed.TryGetValue(ped, out var was) || was.id != ped.Id) dressed[ped] = was = (ped.Id, ped.Outfit);
                    var outfit = was.outfit;
                    if (theme != null) outfit = Costume(outfit, theme, ped.Id);
                    if (Mood == CrowdMood.Angry) outfit.Skin = Vector4.Lerp(outfit.Skin, new Vector4(0.85f, 0.05f, 0.03f, 1f), 0.45f + 0.35f * moodK);
                    if (looks.TryGetValue(ped, out var look) && look.id == ped.Id && look.tints.Count > 0)
                    {
                        var (mix, amount) = look.tints[look.tints.Count - 1];
                        outfit.Top = Vector4.Lerp(outfit.Top, mix, amount);
                        outfit.Under = Vector4.Lerp(outfit.Under, mix, amount);
                        outfit.Bottom = Vector4.Lerp(outfit.Bottom, mix, amount);
                        outfit.Hair = Vector4.Lerp(outfit.Hair, mix, amount);
                        outfit.Skin = Vector4.Lerp(outfit.Skin, mix, amount);
                        if (look.statues > 0) ped.Vel = Vector2.zero;
                    }
                    ped.Outfit = outfit;
                }
                if (sprites == null || marks >= 40 || ped.State >= PedState.Dive) continue;
                float dist = Vector2.Distance(ped.Pos, eye);
                if (dist > 55f || !InView(ped.Pos, dist)) continue;
                marks++;
                var head = ped.Position + Vector3.up * 2.15f;
                switch (Mood)
                {
                    case CrowdMood.Angry: sprites.Mark(EffectSprites.Icon.Anger, head + Vector3.up * Mathf.Abs(Mathf.Sin(now * 7f + ped.Id)) * 0.12f, 0.3f); break;
                    case CrowdMood.Calm: sprites.Mark(EffectSprites.Icon.Heart, head + Vector3.up * Mathf.Repeat(now * 0.4f + ped.Id * 0.13f, 1f) * 0.6f, 0.2f); break;
                    case CrowdMood.Panic: sprites.Mark(EffectSprites.Icon.Alarm, head, 0.28f); break;
                }
            }
        }

        void Undress()
        {
            foreach (var pair in dressed)
                if (pair.Key != null && pair.Key.Id == pair.Value.id) pair.Key.Outfit = pair.Value.outfit;
            dressed.Clear();
        }

        public static bool Busy => Crowd.Count > 0 || moods.Count > 0 || themes.Count > 0 || (Instance != null && Instance.looks.Count > 0);

        readonly Dictionary<Pedestrian, (int id, List<(Vector4 mix, float amount)> tints, int statues)> looks = new Dictionary<Pedestrian, (int, List<(Vector4, float)>, int)>();

        // An object effect's look on one person's clothes; a stone one stands still as a statue.
        public void AddLook(Pedestrian ped, Color mix, float amount, bool statue)
        {
            if (!looks.TryGetValue(ped, out var look) || look.id != ped.Id) look = (ped.Id, new List<(Vector4, float)>(), 0);
            look.tints.Add(((Vector4)mix, amount));
            if (statue)
            {
                look.statues++;
                LeaveSpotState(ped);
                ped.Crossing = -1;
                Unregister(ped);
                ped.Vel = Vector2.zero;
                ped.Lean = 0f;
                Enter(ped, PedState.Startled, 9999f);
            }
            looks[ped] = look;
        }

        public void RemoveLook(Pedestrian ped, bool statue)
        {
            if (!looks.TryGetValue(ped, out var look) || look.id != ped.Id) return;
            if (look.tints.Count > 0) look.tints.RemoveAt(look.tints.Count - 1);
            if (statue && --look.statues == 0 && ped.State == PedState.Startled) Enter(ped, PedState.Startled, 0.2f);
            if (look.tints.Count == 0) looks.Remove(ped);
            else looks[ped] = look;
        }

        static Vector4 Lin(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c.linear;
        }

        static readonly Dictionary<string, Vector4[][]> costumes = new Dictionary<string, Vector4[][]>();

        // Top, under, bottom and hair choices per theme; null keeps the person's own.
        static Vector4[][] Wardrobe(string theme)
        {
            if (costumes.TryGetValue(theme, out var w)) return w;
            Vector4[] L(params string[] hexes) => System.Array.ConvertAll(hexes, Lin);
            w = theme switch
            {
                "tech_bros" => new[] { L("#6C758B", "#1C2347", "#A9ADB4"), L("#9ED7FF", "#F7F8F9"), L("#D8C59A", "#2C4FA3"), null },
                "elvis" => new[] { L("#F4F4F0"), L("#D4AF37"), L("#F4F4F0"), L("#14110F") },
                "delivery_riders" => new[] { L("#E8333A", "#2EC27E"), L("#1A1A22"), L("#1A1A22"), null },
                _ => new[] { L("#FF9C86", "#17B2D0", "#FFE27A", "#8CE3BF", "#FF8A1E"), L("#F7F8F9"), L("#17B2D0", "#FFCB1F", "#FF9C86"), null },
            };
            return costumes[theme] = w;
        }

        static Vector4[] ThemeColors(string theme) => Wardrobe(theme)[0];

        static CrowdRenderer.Outfit Costume(CrowdRenderer.Outfit o, string theme, int id)
        {
            var w = Wardrobe(theme);
            Vector4 Pick(Vector4[] options, int salt) => options[(int)((uint)(id * 2654435761u + salt * 40503u) % (uint)options.Length)];
            o.Top = Pick(w[0], 1);
            o.Under = Pick(w[1], 2);
            o.Bottom = Pick(w[2], 3);
            if (w[3] != null) o.Hair = Pick(w[3], 4);
            return o;
        }
    }
}
