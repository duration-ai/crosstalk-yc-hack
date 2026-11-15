using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Audio
{
    // Maps every GameEvents signal to its sound. The tables here are the event map.
    public sealed class GameSounds : MonoBehaviour
    {
        // Breakable class keyword -> clip, first match wins (CROS-23 passes its class id as Kind).
        static readonly (string key, string clip)[] Breakables =
        {
            ("hydrant", "hydrant_break"), ("traffic", "traffic_light"), ("street_light", "street_light"), ("streetlight", "street_light"),
            ("lamp", "street_light"), ("light", "street_light"), ("trash", "trash_can"), ("bin", "trash_can"), ("newspaper", "newspaper_box"),
            ("meter", "parking_meter"), ("bench", "bench"), ("shelter", "bus_shelter"), ("tree", "tree"), ("fence", "fence"),
            ("cone", "cone"), ("barrier", "barrier"), ("barricade", "barrier"), ("mail", "mailbox"), ("dock", "bike_dock"),
            ("rack", "bike_dock"), ("payphone", "payphone"), ("phone", "payphone"), ("billboard", "billboard"), ("sign", "sign"),
            ("planter", "planter"), ("pot", "planter"), ("crate", "crates"), ("box", "crates"), ("pallet", "crates"),
            ("storefront", "storefront_glass"), ("window", "storefront_glass"), ("glass", "storefront_glass"), ("fruit", "fruit_stand"),
            ("stand", "fruit_stand"),
        };

        static readonly string[] Barks = { "hey", "whoa", "woo_1", "yikes_1", "oops_1" };

        public static readonly Dictionary<Surface, string> ImpactClips = new Dictionary<Surface, string>
        {
            { Surface.Generic, "concrete" }, { Surface.Metal, "metal" }, { Surface.Concrete, "concrete" }, { Surface.Wood, "wood" },
            { Surface.Plastic, "plastic" }, { Surface.Glass, "glass" }, { Surface.Foliage, "foliage" }, { Surface.Body, "body" },
            { Surface.Water, "splash" },
        };

        SoundLoop ring, lineBed;

        public static string BreakableClip(string kind)
        {
            kind = (kind ?? "").ToLowerInvariant();
            foreach (var (key, clip) in Breakables)
            {
                if (kind.Contains(key)) return clip;
            }
            return "generic";
        }

        void OnEnable()
        {
            GameEvents.Impact += OnImpact;
            GameEvents.Smash += OnSmash;
            GameEvents.Wreck += OnWreck;
            GameEvents.PedestrianDive += OnDive;
            GameEvents.Shove += OnShove;
            GameEvents.Countdown += OnCountdown;
            GameEvents.PlaceChanged += OnPlace;
            GameEvents.NewsBar += OnNews;
            GameEvents.Call += OnCall;
            GameEvents.OperatorSpeaking += OnOperator;
            GameEvents.Paused += OnPaused;
            GameEvents.UiTap += OnUiTap;
            GameEvents.UiReady += OnUiReady;
            GameEvents.Boost += OnBoost;
            GameEvents.Horn += OnHorn;
            GameEvents.AskCollected += OnAskCollected;
        }

        void OnDisable()
        {
            GameEvents.Impact -= OnImpact;
            GameEvents.Smash -= OnSmash;
            GameEvents.Wreck -= OnWreck;
            GameEvents.PedestrianDive -= OnDive;
            GameEvents.Shove -= OnShove;
            GameEvents.Countdown -= OnCountdown;
            GameEvents.PlaceChanged -= OnPlace;
            GameEvents.NewsBar -= OnNews;
            GameEvents.Call -= OnCall;
            GameEvents.OperatorSpeaking -= OnOperator;
            GameEvents.Paused -= OnPaused;
            GameEvents.UiTap -= OnUiTap;
            GameEvents.UiReady -= OnUiReady;
            GameEvents.Boost -= OnBoost;
            GameEvents.Horn -= OnHorn;
            GameEvents.AskCollected -= OnAskCollected;
        }

        static float Jitter(float spread = 0.06f) => Random.Range(1f - spread, 1f + spread);

        void OnImpact(ImpactInfo hit)
        {
            float v = Mathf.Clamp(hit.Force / 14f, 0.2f, 1f);
            int priority = hit.InvolvesPlayer ? 220 : 120;
            if (hit.Surface == Surface.Vehicle)
            {
                SoundSystem.Play(hit.Force > 7f ? "crunch_heavy" : "crunch_light", hit.Position, Bus.Sfx, v, Jitter(), priority, 90f);
                if (hit.Force > 13f && Random.value < 0.5f) SoundSystem.Play("glass", hit.Position, Bus.Sfx, v * 0.7f, Jitter(), priority - 20, 70f);
                return;
            }
            SoundSystem.Play(ImpactClips[hit.Surface], hit.Position, Bus.Sfx, v, Jitter() * Mathf.Lerp(1.1f, 0.9f, v), priority, 80f);
            if (hit.InvolvesPlayer && hit.Force > 9f && hit.Surface != Surface.Body && hit.Surface != Surface.Water)
                SoundSystem.Play("crunch_light", hit.Position, Bus.Sfx, v * 0.7f, Jitter(), priority - 10, 80f);
        }

        void OnSmash(SmashInfo smash)
        {
            string clip = BreakableClip(smash.Kind);
            float v = Mathf.Clamp(0.55f + smash.Force / 25f, 0.55f, 1f);
            SoundSystem.Play(clip, smash.Position, Bus.Sfx, v, Jitter(0.05f), smash.ByPlayer ? 230 : 140, 100f);
            if (clip == "hydrant_break")
            {
                var gush = SoundSystem.LoopAt("hydrant_gush", Bus.Sfx, smash.Position + Vector3.up, 50f, 150, 0.8f);
                if (gush != null) StartCoroutine(StopLater(gush, 9f, 3f));
            }
        }

        static System.Collections.IEnumerator StopLater(SoundLoop loop, float after, float fade)
        {
            yield return new WaitForSeconds(after);
            loop.Stop(fade);
        }

        void OnWreck(Vector3 position, bool isPlayer) => SoundSystem.Play("wreck", position, Bus.Sfx, 1f, Jitter(0.04f), isPlayer ? 255 : 160, 150f);

        void OnDive(Vector3 position, int seed)
        {
            string bark = Barks[Mathf.Abs(seed) % Barks.Length];
            SoundSystem.Play(bark, position + Vector3.up * 1.6f, Bus.Sfx, 0.85f, 0.92f + (Mathf.Abs(seed) % 7) * 0.03f, 170, 40f);
        }

        void OnShove(Vector3 position)
        {
            SoundSystem.Play("shove", position + Vector3.up, Bus.Sfx, 0.8f, Jitter(), 180, 30f);
            if (Random.value < 0.6f) OnDive(position, Random.Range(0, 100));
        }

        void OnCountdown(int left)
        {
            if (left > 0) SoundSystem.Play2D("countdown_beep", Bus.Ui, 0.8f, 1f, 255);
            else SoundSystem.Play2D("countdown_go", Bus.Ui, 1f, 1f, 255);
        }

        void OnPlace(int place, int previous) => SoundSystem.Play2D(place < previous ? "place_up" : "place_down", Bus.Ui, 0.7f, 1f, 210);

        void OnNews() => SoundSystem.Play2D("news_whoosh", Bus.Ui, 0.6f, Jitter(0.03f), 190);

        void OnCall(CallState state)
        {
            switch (state)
            {
                case CallState.Ringing:
                    if (ring == null) ring = SoundSystem.Loop("call_ring", Bus.Ui, null, 0f, 250, 0.7f);
                    break;
                case CallState.Answered:
                    ring?.Stop(0.05f);
                    ring = null;
                    SoundSystem.Play2D("payphone_pickup", Bus.Ui, 0.9f, 1f, 250);
                    if (lineBed == null) lineBed = SoundSystem.Loop("operator_line_bed", Bus.Voice, null, 0f, 245, 0.35f);
                    break;
                default:
                    ring?.Stop(0.05f);
                    lineBed?.Stop(0.3f);
                    ring = lineBed = null;
                    SoundSystem.SetDuck(this, false);
                    SoundSystem.Play2D("call_hangup", Bus.Ui, 0.8f, 1f, 240);
                    break;
            }
        }

        void OnOperator(bool speaking)
        {
            SoundSystem.SetDuck(this, speaking);
            if (speaking) SoundSystem.Play2D("operator_on", Bus.Voice, 0.5f, 1f, 240);
        }

        void OnPaused(bool paused)
        {
            AudioListener.pause = paused;
            SoundSystem.Play2D(paused ? "pause_open" : "pause_close", Bus.Ui, 0.7f, 1f, 255);
        }

        void OnUiTap() => SoundSystem.Play2D("ui_tap", Bus.Ui, 0.6f, Jitter(0.03f), 200);

        // An ask pickup: the combo chime a fifth up, so it reads as a reward but not a smash.
        void OnAskCollected(Vector3 position) => SoundSystem.Play2D("combo_chime", Bus.Ui, 0.8f, 1.5f, 220);

        void OnUiReady() => SoundSystem.Play2D("ui_ready", Bus.Ui, 0.8f, 1f, 230);

        void OnBoost(bool on)
        {
            if (on) SoundSystem.Play2D("boost_whoosh", Bus.Sfx, 0.8f, 1f, 230);
        }

        void OnHorn(Transform vehicle) => VehicleSounds.Honk(vehicle);
    }
}
