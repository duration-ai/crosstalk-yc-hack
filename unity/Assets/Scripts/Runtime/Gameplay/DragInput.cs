using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Touch input. In the car the whole screen is one drag stick: the drag vector from the
    // touch-down point gives direction (screen up = away from the camera) and strength (longer =
    // faster). On foot (Split) the screen is split like most mobile third-person games: a drag
    // that starts on the left half is the move stick, whose anchor trails the thumb once it passes
    // full reach, and a drag that starts on the right half pans the camera, so both thumbs work at
    // once. A short touch that barely moves is a tap anywhere. The mouse stands in for touch in the
    // editor.
    public sealed class DragInput : MonoBehaviour
    {
        const float TapSeconds = 0.35f;
        const int MouseId = 1000;

        struct Finger
        {
            public int Id;
            public Vector2 Start;
            public Vector2 Last;
            public float DownTime;
            public float MaxTravel;
        }

        public bool Split;
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
        Vector2 lookDelta;

        public float TapSlop => Mathf.Min(Screen.width, Screen.height) * 0.035f;
        public float Reach => Mathf.Min(Screen.width, Screen.height) * 0.25f;

        public Vector2 Direction
        {
            get
            {
                var d = Current - Anchor;
                return Dragging && d.magnitude > TapSlop ? d.normalized : Vector2.zero;
            }
        }

        public float Strength
        {
            get
            {
                if (!Dragging) return 0f;
                float m = (Current - Anchor).magnitude;
                return Mathf.Clamp01((m - TapSlop) / Mathf.Max(Reach - TapSlop, 1f));
            }
        }

        // Camera pan in screen pixels since the last call.
        public Vector2 TakeLook()
        {
            var d = lookDelta;
            lookDelta = Vector2.zero;
            return d;
        }

        // Drops the gestures in progress without a tap, so a finger already down when the game
        // pauses neither keeps steering nor taps on release.
        public void Cancel()
        {
            move.Id = -1;
            look.Id = -1;
            Dragging = false;
            lookDelta = Vector2.zero;
        }

        void Update()
        {
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
            if ((IgnoreAt != null && IgnoreAt(position)) || (IgnoreTouch != null && IgnoreTouch(position))) return;
            var finger = new Finger { Id = id, Start = position, Last = position, DownTime = Time.unscaledTime };
            if (Split && position.x > Screen.width * 0.5f)
            {
                if (look.Id < 0) look = finger;
                return;
            }
            if (move.Id >= 0) return;
            move = finger;
            Dragging = true;
            Anchor = Current = position;
        }

        void Move(int id, Vector2 position)
        {
            if (id == look.Id)
            {
                lookDelta += position - look.Last;
                look.Last = position;
                look.MaxTravel = Mathf.Max(look.MaxTravel, (position - look.Start).magnitude);
                return;
            }
            if (id != move.Id) return;
            Current = position;
            var offset = position - Anchor;
            move.MaxTravel = Mathf.Max(move.MaxTravel, (position - move.Start).magnitude);
            if (Split && offset.magnitude > Reach) Anchor = position - offset.normalized * Reach;
        }

        void End(int id, Vector2 position)
        {
            if (id != move.Id && id != look.Id) return;
            Move(id, position);
            ref var finger = ref id == move.Id ? ref move : ref look;
            bool tap = finger.MaxTravel < TapSlop && Time.unscaledTime - finger.DownTime < TapSeconds;
            if (id == move.Id) Dragging = false;
            finger.Id = -1;
            if (tap && (IgnoreAt == null || !IgnoreAt(position))) Tapped?.Invoke(position);
        }
    }
}
