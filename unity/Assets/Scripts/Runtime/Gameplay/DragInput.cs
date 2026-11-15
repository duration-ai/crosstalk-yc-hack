using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Touch input. One fixed stick sits in the bottom-right corner (HudLayout.StickScreen): a touch
    // that starts on it moves you, its offset from the stick's centre giving direction (up = away
    // from the camera on foot, the car's nose at the wheel) and strength (further = faster), and
    // it never drifts, so you always know where it is. A drag that starts anywhere else pans the
    // camera, on foot and in a car. A quick touch anywhere off the stick is a tap (jump, get in or
    // out), with any finger, so you can jump while running. Touches on the minimap are left to it.
    // The mouse stands in for touch.
    public sealed class DragInput : MonoBehaviour
    {
        const int MouseId = 1000;
        const int HeldId = -2;
        const float TapSeconds = 0.3f;

        struct Finger
        {
            public int Id;
            public Vector2 Start;
            public Vector2 Last;
            public float DownTime;
            public float MaxTravel;
        }

        public bool Dragging { get; private set; }
        public Vector2 Anchor { get; private set; }
        public Vector2 Current { get; private set; }
        public event Action<Vector2> Tapped;
        public Func<Vector2, bool> IgnoreTouch;

        // Touches that begin where this returns true (the HUD's pause button, or anywhere while
        // paused) are left to the HUD and never start a drag or a tap.
        public Func<Vector2, bool> IgnoreAt;

        Finger move = new Finger { Id = -1 };
        Finger look = new Finger { Id = -1 };
        readonly List<Finger> extra = new List<Finger>();
        Vector2 lookDelta;

        public float Reach => HudLayout.StickRadius;
        float DeadZone => Reach * 0.15f;
        static float TapPixels => Mathf.Min(Screen.width, Screen.height) * 0.02f;

        public Vector2 Direction
        {
            get
            {
                var d = Current - Anchor;
                return Dragging && d.magnitude > DeadZone ? d.normalized : Vector2.zero;
            }
        }

        public float Strength
        {
            get
            {
                if (!Dragging) return 0f;
                float m = (Current - Anchor).magnitude;
                return Mathf.Clamp01((m - DeadZone) / Mathf.Max(Reach - DeadZone, 1f));
            }
        }

        // Camera pan in screen pixels since the last call.
        public Vector2 TakeLook()
        {
            var d = lookDelta;
            lookDelta = Vector2.zero;
            return d;
        }

        // Scripted captures: hold the stick at an offset in stick radii, or let go.
        public void Hold(Vector2 offset)
        {
            move.Id = HeldId;
            Dragging = true;
            Anchor = HudLayout.StickScreen;
            Current = Anchor + Vector2.ClampMagnitude(offset, 1f) * Reach;
        }

        public void Release()
        {
            if (move.Id == HeldId) Cancel();
        }

        // Drops the gestures in progress without a tap, so a finger already down when the game
        // pauses or the screen rotates neither keeps steering nor taps on release.
        public void Cancel()
        {
            move.Id = -1;
            look.Id = -1;
            extra.Clear();
            Dragging = false;
            lookDelta = Vector2.zero;
        }

        void OnEnable() => ScreenShape.Changed += Cancel;

        void OnDisable() => ScreenShape.Changed -= Cancel;

        void Update()
        {
            if (move.Id == HeldId) return;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began) Begin(t.fingerId, t.position);
                    else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary) Move(t.fingerId, t.position);
                    else End(t.fingerId, t.position);
                }
                return;
            }
            extra.Clear();
            if (move.Id >= 0 && move.Id != MouseId) End(move.Id, Current);
            if (look.Id >= 0 && look.Id != MouseId) End(look.Id, look.Last);

            bool held = Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0);
            bool tracked = move.Id == MouseId || look.Id == MouseId;
            Vector2 mouse = Input.mousePosition;
            if (held && !tracked) Begin(MouseId, mouse);
            else if (held) Move(MouseId, mouse);
            else if (tracked) End(MouseId, mouse);
        }

        void Begin(int id, Vector2 position)
        {
            if ((IgnoreAt != null && IgnoreAt(position)) || (IgnoreTouch != null && IgnoreTouch(position)) || (Map.Minimap.Shown && HudLayout.InMinimap(position))) return;
            var finger = new Finger { Id = id, Start = position, Last = position, DownTime = Time.unscaledTime };
            if (move.Id < 0 && HudLayout.InStick(position))
            {
                move = finger;
                Dragging = true;
                Anchor = HudLayout.StickScreen;
                Current = position;
                return;
            }
            if (look.Id < 0) look = finger;
            else extra.Add(finger);
        }

        void Move(int id, Vector2 position)
        {
            if (id == move.Id)
            {
                Current = position;
                return;
            }
            if (id == look.Id)
            {
                look.MaxTravel = Mathf.Max(look.MaxTravel, (position - look.Start).magnitude);
                if (look.MaxTravel >= TapPixels) lookDelta += position - look.Last;
                look.Last = position;
                return;
            }
            int e = ExtraIndex(id);
            if (e < 0) return;
            var f = extra[e];
            f.MaxTravel = Mathf.Max(f.MaxTravel, (position - f.Start).magnitude);
            extra[e] = f;
        }

        void End(int id, Vector2 position)
        {
            if (id == move.Id)
            {
                move.Id = -1;
                Dragging = false;
                return;
            }
            Move(id, position);
            Finger finger;
            int e = ExtraIndex(id);
            if (e >= 0)
            {
                finger = extra[e];
                extra.RemoveAt(e);
            }
            else if (id == look.Id)
            {
                finger = look;
                look.Id = -1;
            }
            else return;
            bool tap = finger.MaxTravel < TapPixels && Time.unscaledTime - finger.DownTime < TapSeconds;
            if (tap && (IgnoreAt == null || !IgnoreAt(position))) Tapped?.Invoke(position);
        }

        int ExtraIndex(int id)
        {
            for (int i = 0; i < extra.Count; i++)
                if (extra[i].Id == id) return i;
            return -1;
        }
    }
}
