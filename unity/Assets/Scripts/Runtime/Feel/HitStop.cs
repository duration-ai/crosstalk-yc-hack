using UnityEngine;

namespace Dogpatch.Feel
{
    // A brief near-freeze of game time on big smashes. Leaves time alone while paused and only
    // restores the scale it replaced, so it never fights a pause menu or slow-mo.
    public sealed class HitStop : MonoBehaviour
    {
        const float Scale = 0.05f;
        static HitStop instance;
        float until;
        float restore = 1f;
        bool active;

        public static void Trigger(float seconds)
        {
            if (instance == null) instance = new GameObject("HitStop").AddComponent<HitStop>();
            instance.Begin(seconds);
        }

        void Begin(float seconds)
        {
            if (Time.timeScale == 0f) return;
            if (!active)
            {
                restore = Time.timeScale;
                active = true;
            }
            Time.timeScale = Scale;
            until = Mathf.Max(until, Time.unscaledTime + seconds);
        }

        void Update()
        {
            if (!active || Time.unscaledTime < until) return;
            active = false;
            if (Mathf.Approximately(Time.timeScale, Scale)) Time.timeScale = restore;
        }
    }
}
