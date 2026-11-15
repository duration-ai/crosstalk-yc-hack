using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using UnityEngine;

namespace Dogpatch.Race
{
    // Swipe at someone to shove them: a quick flick on foot shoves the nearest IShoveable in that
    // direction, within reach. The flick also feeds the drag control, so the player lunges a step.
    public sealed class ShoveInput : MonoBehaviour
    {
        const float Reach = 3.6f;
        const float MaxSeconds = 0.3f;
        const float ShoveSpeed = 7.5f;

        public GameController Game;
        public Func<Vector2, bool> IgnoreTouch;

        readonly Dictionary<int, (Vector2 start, float time)> touches = new Dictionary<int, (Vector2, float)>();

        void OnEnable() => ScreenShape.Changed += touches.Clear;

        void OnDisable() => ScreenShape.Changed -= touches.Clear;

        void Update()
        {
            if (Game == null || !Game.IsReady) return;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && !HudLayout.InStick(t.position) && (IgnoreTouch == null || !IgnoreTouch(t.position))) touches[t.fingerId] = (t.position, Time.unscaledTime);
                if ((t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) && touches.TryGetValue(t.fingerId, out var s))
                {
                    touches.Remove(t.fingerId);
                    Flick(s.start, t.position, Time.unscaledTime - s.time);
                }
            }
            if (Input.touchCount > 0) return;
            if (Input.GetMouseButtonDown(0) && !HudLayout.InStick(Input.mousePosition) && (IgnoreTouch == null || !IgnoreTouch(Input.mousePosition))) touches[-1] = (Input.mousePosition, Time.unscaledTime);
            if (Input.GetMouseButtonUp(0) && touches.TryGetValue(-1, out var m))
            {
                touches.Remove(-1);
                Flick(m.start, Input.mousePosition, Time.unscaledTime - m.time);
            }
        }

        void Flick(Vector2 from, Vector2 to, float seconds)
        {
            var delta = to - from;
            if (seconds > MaxSeconds || delta.magnitude < Mathf.Min(Screen.width, Screen.height) * 0.08f) return;
            var cam = Game.View.transform;
            var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            ShoveToward((right * delta.x + forward * delta.y).normalized);
        }

        // Shoves whoever is nearest in that world direction; false when nobody is in reach.
        public bool ShoveToward(Vector3 direction)
        {
            if (Game.CurrentCar != null) return false;
            var me = Game.Player.transform;
            IShoveable best = null;
            float bestScore = float.MaxValue;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (!(behaviour is IShoveable target) || behaviour.transform == me || !target.CanBeShoved) continue;
                var offset = target.Position - me.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance > Reach || Vector3.Angle(direction, offset) > 70f) continue;
                if (distance >= bestScore) continue;
                bestScore = distance;
                best = target;
            }
            if (best == null) return false;
            var push = best.Position - me.position;
            push.y = 0f;
            best.Shove((push.normalized + direction).normalized * ShoveSpeed, Game.Player.gameObject);
            return true;
        }
    }
}
