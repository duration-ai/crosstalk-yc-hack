using Dogpatch.Audio;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Feel
{
    // Turns game events into camera shake, hit-stop, haptics and the landing squash.
    public sealed class FeelDirector : MonoBehaviour
    {
        PlayerController player;
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<FeelDirector>() != null) return;
            var go = new GameObject("Feel");
            DontDestroyOnLoad(go);
            go.AddComponent<FeelDirector>();
        }

        void OnEnable()
        {
            GameEvents.Impact += OnImpact;
            GameEvents.Smash += OnSmash;
            GameEvents.Wreck += OnWreck;
            GameEvents.Countdown += OnCountdown;
            GameEvents.Call += OnCall;
            GameEvents.Results += OnResults;
            GameEvents.PlaceChanged += OnPlace;
            GameEvents.Boost += OnBoost;
            GameEvents.Shove += OnShove;
        }

        void OnDisable()
        {
            GameEvents.Impact -= OnImpact;
            GameEvents.Smash -= OnSmash;
            GameEvents.Wreck -= OnWreck;
            GameEvents.Countdown -= OnCountdown;
            GameEvents.Call -= OnCall;
            GameEvents.Results -= OnResults;
            GameEvents.PlaceChanged -= OnPlace;
            GameEvents.Boost -= OnBoost;
            GameEvents.Shove -= OnShove;
            if (player != null) player.Landed -= OnLanded;
        }

        void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<CameraFeel>() == null) cam.gameObject.AddComponent<CameraFeel>();
            if (player != null) return;
            player = FindAnyObjectByType<PlayerController>();
            if (player != null) player.Landed += OnLanded;
        }

        static void Shake(float amount)
        {
            if (CameraFeel.Instance != null) CameraFeel.Instance.AddTrauma(amount);
        }

        void OnImpact(ImpactInfo hit)
        {
            if (!hit.InvolvesPlayer) return;
            float k = Mathf.Clamp01(hit.Force / 20f);
            Shake(0.15f + 0.6f * k);
            Haptics.Impact(k > 0.6f ? HapticStyle.Heavy : k > 0.3f ? HapticStyle.Medium : HapticStyle.Light, 0.5f + 0.5f * k);
            if (hit.Force > 16f) HitStop.Trigger(0.05f);
        }

        void OnSmash(SmashInfo smash)
        {
            if (!smash.ByPlayer) return;
            bool big = smash.Force > 12f || GameSounds.BreakableClip(smash.Kind) is "bus_shelter" or "storefront_glass" or "billboard" or "hydrant_break";
            Shake(big ? 0.5f : 0.28f);
            Haptics.Impact(big ? HapticStyle.Heavy : HapticStyle.Rigid, big ? 1f : 0.7f);
            if (big) HitStop.Trigger(0.07f);
        }

        void OnWreck(Vector3 position, bool isPlayer)
        {
            if (!isPlayer) return;
            Shake(1f);
            HitStop.Trigger(0.12f);
            Haptics.Impact(HapticStyle.Heavy, 1f);
        }

        void OnCountdown(int left)
        {
            if (left > 0) Haptics.Impact(HapticStyle.Light, 0.8f);
            else
            {
                Haptics.Impact(HapticStyle.Heavy, 1f);
                Shake(0.25f);
            }
        }

        void OnCall(CallState state)
        {
            if (state == CallState.Ringing) Haptics.Notify(HapticNotice.Warning);
            else if (state == CallState.Answered) Haptics.Impact(HapticStyle.Medium, 0.8f);
        }

        void OnResults(int place) => Haptics.Notify(place == 1 ? HapticNotice.Success : HapticNotice.Warning);

        void OnPlace(int place, int previous)
        {
            if (place < previous) Haptics.Selection();
        }

        void OnBoost(bool on)
        {
            if (!on) return;
            if (CameraFeel.Instance != null) CameraFeel.Instance.Kick(4f);
            Haptics.Impact(HapticStyle.Soft, 0.8f);
        }

        void OnShove(Vector3 position) => Haptics.Impact(HapticStyle.Light, 0.6f);

        // Squash the character on touchdown; PlayerController's own scale easing springs it back.
        void OnLanded(float fallSpeed)
        {
            if (player == null || player.Visual == null) return;
            float s = Mathf.Clamp(fallSpeed / 22f, 0.1f, 0.32f);
            player.Visual.localScale = new Vector3(1f + s * 0.8f, 1f - s, 1f + s * 0.8f);
            if (fallSpeed > 8f) Haptics.Impact(HapticStyle.Light, 0.6f);
        }
    }
}
