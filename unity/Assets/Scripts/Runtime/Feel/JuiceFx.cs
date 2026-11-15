using System;
using System.Collections;
using System.IO;
using Dogpatch.Audio;
using Dogpatch.Look;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Feel
{
    // Screen-space juice on top of the Slipstream HUD, all flat parallelograms in the guide's palette:
    // the world-change whoosh (Burnout Paradise's takedown wipe, Mario Kart's item hit), the ask
    // pickup burst (Mario Kart's item box), results confetti (Mario Kart / Crash Team Racing podium),
    // the GO! starburst (NFS Unbound), and hit sparks plus an edge flash on hard player impacts
    // (Burnout Paradise). IMGUI quads only, drawn on Repaint, on unscaled time so hit-stop never
    // freezes them; counts follow EffectsSettings.Intensity.
    public sealed class JuiceFx : MonoBehaviour
    {
        const int MaxBits = 160;

        struct Bit
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Angle;
            public float Spin;
            public float Born;
            public float Life;
            public float Width;
            public float Height;
            public float Gravity;
            public float Drag;
            public Color Color;
        }

        static readonly Color Ink = Hex("#1C2347");
        static readonly Color Sun = Hex("#FFCB1F");
        static readonly Color Pink = Hex("#FF3E8E");
        static readonly Color Paper = Hex("#F7F8F9");
        static readonly Color[] Confetti =
        {
            Hex("#FFCB1F"), Hex("#FF3E8E"), Hex("#2D8CFF"), Hex("#A35BFF"), Hex("#FF8A1E"), Hex("#2EC27E"), Hex("#FFFFFF"),
        };

        readonly Bit[] bits = new Bit[MaxBits];
        int nextBit;
        Texture2D white;
        Font font;
        GUIStyle style;
        RaceDirector race;
        float nextScan;

        float whooshAt = -10f;
        Color whooshColor;
        float pickupAt = -10f;
        Vector2 pickupAt2;
        float goAt = -10f;
        float flashAt = -10f;
        float flashStrength;

        static float Now => Time.unscaledTime;
        static float U => Gameplay.HudLayout.U;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<JuiceFx>() != null) return;
            var go = new GameObject("Juice");
            DontDestroyOnLoad(go);
            var fx = go.AddComponent<JuiceFx>();
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-juice-capture");
            if (at >= 0) fx.StartCoroutine(fx.Capture(at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "juice")));
        }

        // Headless stills of each effect: run the player with -juice-capture <folder>.
        IEnumerator Capture(string folder)
        {
            Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            while (RaceDirector.Instance == null || !RaceDirector.Instance.Setup) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            var shots = new (string name, Action fire, float after)[]
            {
                ("1-whoosh", () => OnRaceEvent(new RaceEvent { news = "MAYA LOWERED GRAVITY", color = "purple" }), 0.16f),
                ("2-pickup", () => OnPickup(Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 12f : Vector3.zero), 0.12f),
                ("3-hit-flash", () => OnImpact(new ImpactInfo { Position = Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 8f : Vector3.zero, Force = 22f, Surface = Surface.Vehicle, InvolvesPlayer = true }), 0.05f),
                ("4-go-burst", () => OnCountdown(0), 0.15f),
                ("5-confetti", () => OnResults(1), 1.2f),
                ("6-boost", () => OnBoost(true), 0.08f),
                ("7-wreck", () => OnWreck(Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 10f : Vector3.zero, true), 0.1f),
            };
            foreach (var shot in shots)
            {
                shot.fire();
                yield return new WaitForSecondsRealtime(shot.after);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, shot.name + ".png"));
                Debug.Log("[Juice] captured " + shot.name);
                yield return new WaitForSecondsRealtime(1.5f);
            }
            Debug.Log("[Juice] capture done");
            Application.Quit();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        void Awake()
        {
            white = Texture2D.whiteTexture;
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
        }

        void OnEnable()
        {
            GameEvents.AskCollected += OnPickup;
            GameEvents.Results += OnResults;
            GameEvents.Countdown += OnCountdown;
            GameEvents.Impact += OnImpact;
            GameEvents.Boost += OnBoost;
            GameEvents.Wreck += OnWreck;
        }

        void OnDisable()
        {
            GameEvents.AskCollected -= OnPickup;
            GameEvents.Results -= OnResults;
            GameEvents.Countdown -= OnCountdown;
            GameEvents.Impact -= OnImpact;
            GameEvents.Boost -= OnBoost;
            GameEvents.Wreck -= OnWreck;
            if (race != null) race.EventArrived -= OnRaceEvent;
        }

        void Update()
        {
            if (race != null || Now < nextScan) return;
            nextScan = Now + 0.5f;
            race = RaceDirector.Instance;
            if (race != null) race.EventArrived += OnRaceEvent;
        }

        // ---- triggers ----

        void OnRaceEvent(RaceEvent e)
        {
            if (string.IsNullOrEmpty(e.news)) return;
            whooshAt = Now;
            whooshColor = e.color == "purple" ? Hex("#A35BFF") : e.color == "orange" ? Hex("#FF8A1E") : e.color == "green" ? Hex("#2EC27E") : e.color == "blue" ? Hex("#2D8CFF") : Pink;
        }

        void OnPickup(Vector3 world)
        {
            var cam = Camera.main;
            Vector2 at = new Vector2(Screen.width * 0.5f, Screen.height * 0.62f);
            if (cam != null)
            {
                var sp = cam.WorldToScreenPoint(world + Vector3.up * 1.6f);
                if (sp.z > 0f) at = new Vector2(Mathf.Clamp(sp.x, 0f, Screen.width), Mathf.Clamp(Screen.height - sp.y, 0f, Screen.height));
            }
            pickupAt = Now;
            pickupAt2 = at;
            int n = EffectsSettings.Count(18);
            for (int i = 0; i < n; i++)
            {
                float a = (i + UnityEngine.Random.value * 0.6f) / n * Mathf.PI * 2f;
                float speed = UnityEngine.Random.Range(380f, 720f) * U;
                Spawn(new Bit
                {
                    Position = at,
                    Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed,
                    Angle = a * Mathf.Rad2Deg,
                    Spin = UnityEngine.Random.Range(-360f, 360f),
                    Life = UnityEngine.Random.Range(0.35f, 0.6f),
                    Width = UnityEngine.Random.Range(14f, 24f) * U,
                    Height = UnityEngine.Random.Range(5f, 8f) * U,
                    Gravity = 600f * U,
                    Drag = 4f,
                    Color = i % 3 == 0 ? Sun : i % 3 == 1 ? Pink : Paper,
                });
            }
        }

        void OnResults(int place)
        {
            if (place <= 0) return;
            int n = EffectsSettings.Count(place == 1 ? 110 : place == 2 ? 70 : 45);
            for (int i = 0; i < n; i++)
            {
                Spawn(new Bit
                {
                    Position = new Vector2(UnityEngine.Random.Range(0f, Screen.width), UnityEngine.Random.Range(-260f, -10f) * U),
                    Velocity = new Vector2(UnityEngine.Random.Range(-60f, 60f), UnityEngine.Random.Range(80f, 220f)) * U,
                    Angle = UnityEngine.Random.Range(0f, 360f),
                    Spin = UnityEngine.Random.Range(-540f, 540f),
                    Born = UnityEngine.Random.Range(0f, 0.9f),
                    Life = UnityEngine.Random.Range(2.6f, 3.8f),
                    Width = UnityEngine.Random.Range(9f, 16f) * U,
                    Height = UnityEngine.Random.Range(5f, 9f) * U,
                    Gravity = 160f * U,
                    Drag = 0.9f,
                    Color = Confetti[UnityEngine.Random.Range(0, Confetti.Length)],
                });
            }
            if (place == 1) goAt = Now;
        }

        void OnCountdown(int left)
        {
            if (left == 0) goAt = Now;
        }

        void OnImpact(ImpactInfo hit)
        {
            if (!hit.InvolvesPlayer || hit.Force < 6f || hit.Surface == Surface.Body) return;
            var cam = Camera.main;
            var normal = cam != null ? (cam.transform.position - hit.Position).normalized : Vector3.up;
            MotionFx.Scrape(hit.Position, normal + Vector3.up, Mathf.Clamp01(hit.Force / 20f));
            if (hit.Force < 14f) return;
            flashAt = Now;
            flashStrength = Mathf.Clamp01((hit.Force - 10f) / 14f);
        }

        // Boost: sunshine streaks shoot outward from the screen edges, never through the middle.
        void OnBoost(bool on)
        {
            if (!on) return;
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
            float reach = Mathf.Max(Screen.width, Screen.height) * 0.5f;
            int n = EffectsSettings.Count(16);
            for (int i = 0; i < n; i++)
            {
                float a = (i + UnityEngine.Random.value * 0.5f) / n * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Spawn(new Bit
                {
                    Position = c + dir * reach * UnityEngine.Random.Range(0.4f, 0.6f),
                    Velocity = dir * UnityEngine.Random.Range(450f, 750f) * U,
                    Angle = a * Mathf.Rad2Deg,
                    Life = UnityEngine.Random.Range(0.22f, 0.34f),
                    Width = UnityEngine.Random.Range(60f, 110f) * U,
                    Height = 5f * U,
                    Color = i % 3 == 0 ? Paper : Sun,
                });
            }
        }

        // Your car is wrecked: the strongest edge flash and a burst of ink, paper and sunshine shards
        // from the car (Burnout Paradise's crash).
        void OnWreck(Vector3 position, bool isPlayer)
        {
            if (!isPlayer) return;
            flashAt = Now;
            flashStrength = 1f;
            var cam = Camera.main;
            Vector2 at = new Vector2(Screen.width * 0.5f, Screen.height * 0.6f);
            if (cam != null)
            {
                var sp = cam.WorldToScreenPoint(position + Vector3.up);
                if (sp.z > 0f) at = new Vector2(Mathf.Clamp(sp.x, 0f, Screen.width), Mathf.Clamp(Screen.height - sp.y, 0f, Screen.height));
            }
            int n = EffectsSettings.Count(26);
            for (int i = 0; i < n; i++)
            {
                float a = (i + UnityEngine.Random.value) / n * Mathf.PI * 2f;
                Spawn(new Bit
                {
                    Position = at,
                    Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a) - 0.4f) * UnityEngine.Random.Range(500f, 1000f) * U,
                    Angle = a * Mathf.Rad2Deg,
                    Spin = UnityEngine.Random.Range(-720f, 720f),
                    Life = UnityEngine.Random.Range(0.5f, 0.9f),
                    Width = UnityEngine.Random.Range(14f, 30f) * U,
                    Height = UnityEngine.Random.Range(8f, 14f) * U,
                    Gravity = 1400f * U,
                    Drag = 2.5f,
                    Color = i % 3 == 0 ? Ink : i % 3 == 1 ? Paper : Sun,
                });
            }
        }

        void Spawn(Bit b)
        {
            b.Born += Now;
            bits[nextBit] = b;
            nextBit = (nextBit + 1) % MaxBits;
        }

        // ---- drawing ----

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = -20;
            Flash();
            Whoosh();
            GoBurst();
            Bits();
            PickupPop();
            GUI.color = Color.white;
        }

        void Quad(Vector2 center, float w, float h, float angle, float lean, Color color)
        {
            var m = GUI.matrix;
            var shear = Matrix4x4.identity;
            shear.m01 = -lean;
            GUI.matrix = m * Matrix4x4.Translate(center) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)) * shear * Matrix4x4.Translate(-center);
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h), white);
            GUI.matrix = m;
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        // A hard hit: a white frame around the screen edge for a blink, never in the middle.
        void Flash()
        {
            float t = (Now - flashAt) / 0.16f;
            if (t >= 1f) return;
            float a = (1f - t) * (0.35f + 0.45f * flashStrength);
            float e = (18f + 22f * flashStrength) * U;
            GUI.color = Alpha(Color.white, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, e), white);
            GUI.DrawTexture(new Rect(0f, Screen.height - e, Screen.width, e), white);
            GUI.DrawTexture(new Rect(0f, e, e, Screen.height - 2f * e), white);
            GUI.DrawTexture(new Rect(Screen.width - e, e, e, Screen.height - 2f * e), white);
        }

        // A world change landed: three leaning stripes in the asker's colour and an ink band sweep the
        // screen right to left in about a third of a second, behind the news bar's own shoot-in.
        void Whoosh()
        {
            float since = Now - whooshAt;
            if (since > 0.6f) return;
            float w = Screen.width;
            float h = Screen.height;
            for (int i = 0; i < 4; i++)
            {
                float delay = i * 0.045f;
                float p = Mathf.Clamp01((since - delay) / 0.34f);
                if (p <= 0f || p >= 1f) continue;
                float ease = p * p * (3f - 2f * p);
                float length = w * (i == 1 ? 0.95f : 0.65f);
                float x = Mathf.Lerp(w + length, -length, ease);
                float y = h * (i == 0 ? 0.2f : i == 1 ? 0.5f : i == 2 ? 0.72f : 0.34f);
                float thick = (i == 1 ? 46f : i == 3 ? 10f : 16f) * U;
                var color = i == 1 ? Alpha(Ink, 0.55f) : Alpha(whooshColor, 0.85f);
                float fade = Mathf.Sin(p * Mathf.PI);
                Quad(new Vector2(x, y), length, thick, -8f, 0.9f, Alpha(color, fade));
                if (i != 1) Quad(new Vector2(x + length * 0.62f, y + thick * 0.9f), length * 0.35f, thick * 0.35f, -8f, 0.9f, Alpha(color, fade * 0.7f));
            }
            float edge = Mathf.Clamp01(1f - since / 0.3f);
            if (edge > 0f)
            {
                float e = 10f * U;
                GUI.color = Alpha(whooshColor, edge * 0.7f);
                GUI.DrawTexture(new Rect(0f, 0f, w, e), white);
                GUI.DrawTexture(new Rect(0f, h - e, w, e), white);
            }
        }

        // GO! and a first place: sunshine rays fly out from behind the slam, then clear the middle.
        void GoBurst()
        {
            float since = Now - goAt;
            if (since > 0.55f) return;
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.42f);
            float p = since / 0.55f;
            float ease = 1f - (1f - p) * (1f - p);
            int rays = 14;
            for (int i = 0; i < rays; i++)
            {
                float a = i * 360f / rays + 7f;
                float dist = Mathf.Lerp(50f, 260f, ease) * U;
                float len = Mathf.Lerp(120f, 40f, ease) * U;
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                Quad(c + dir * dist, len, 9f * U, a, 0f, Alpha(i % 2 == 0 ? Sun : Paper, 1f - p));
            }
        }

        void Bits()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < MaxBits; i++)
            {
                ref var b = ref bits[i];
                if (b.Life <= 0f) continue;
                float age = Now - b.Born;
                if (age < 0f) continue;
                if (age > b.Life)
                {
                    b.Life = 0f;
                    continue;
                }
                b.Velocity *= Mathf.Exp(-b.Drag * dt);
                b.Velocity.y += b.Gravity * dt;
                b.Position += b.Velocity * dt;
                b.Angle += b.Spin * dt;
                float fade = Mathf.Clamp01((b.Life - age) / 0.3f);
                float flip = Mathf.Abs(Mathf.Cos((b.Angle + i * 37f) * Mathf.Deg2Rad * 1.7f));
                Quad(b.Position, b.Width, b.Height * Mathf.Max(0.25f, flip), b.Angle, 0.3f, Alpha(b.Color, fade));
            }
        }

        // The ask pickup: an expanding ring of leaning dashes and a "+1" that pops with overshoot and rises.
        void PickupPop()
        {
            float since = Now - pickupAt;
            if (since > 0.8f) return;
            float p = Mathf.Clamp01(since / 0.35f);
            float radius = Mathf.Lerp(20f, 120f, 1f - (1f - p) * (1f - p)) * U;
            int dashes = 16;
            for (int i = 0; i < dashes && p < 1f; i++)
            {
                float a = i * 360f / dashes;
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                Quad(pickupAt2 + dir * radius, 16f * U * (1f - p * 0.6f), 6f * U, a + 90f, 0f, Alpha(Pink, 1f - p));
            }
            float pop = Overshoot(Mathf.Clamp01(since / 0.28f));
            float fade = Mathf.Clamp01((0.8f - since) / 0.25f);
            if (style == null) style = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(44f * U * Mathf.Max(0.01f, pop)));
            var at = pickupAt2 + new Vector2(0f, -40f * U - since * 90f * U);
            var r = new Rect(at.x - 100f, at.y - 40f, 200f, 80f);
            GUI.color = Color.white;
            style.normal.textColor = TextColor(Ink, fade);
            float o = 2.4f * U;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o, r.width, r.height), "+1", style);
            }
            style.normal.textColor = TextColor(Sun, fade);
            GUI.Label(r, "+1", style);
        }

        // Back-out easing: overshoots about 10% before settling at 1.
        public static float Overshoot(float t)
        {
            const float k = 2.2f;
            t -= 1f;
            return 1f + t * t * ((k + 1f) * t + k);
        }

        static Color TextColor(Color c, float alpha)
        {
            var g = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
            return new Color(g.r, g.g, g.b, c.a * alpha);
        }
    }
}
