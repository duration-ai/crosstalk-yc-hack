using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // The game's only control: touch anywhere and drag. The drag vector from the touch-down
    // point gives direction (screen up = away from the camera) and strength (longer = faster).
    // A short touch that barely moves is a tap. The mouse stands in for touch in the editor.
    public sealed class DragInput : MonoBehaviour
    {
        const float TapSeconds = 0.35f;

        public bool Dragging { get; private set; }
        public Vector2 Anchor { get; private set; }
        public Vector2 Current { get; private set; }
        public event Action<Vector2> Tapped;

        // Touches that begin where this returns true (the HUD's pause button, or anywhere while
        // paused) are left to the HUD and never start a drag or a tap.
        public Func<Vector2, bool> IgnoreAt;

        int fingerId = -1;
        float downTime;
        float maxTravel;

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

        void Update()
        {
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (fingerId < 0 && t.phase == TouchPhase.Began) Begin(t.fingerId, t.position);
                    if (t.fingerId != fingerId) continue;
                    if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary) Move(t.position);
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) End(t.position);
                }
                return;
            }
            if (fingerId >= 0 && fingerId != 1000) End(Current);

            if (fingerId < 0 && (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0))) Begin(1000, Input.mousePosition);
            if (fingerId != 1000) return;
            if (Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) Move(Input.mousePosition);
            else End(Input.mousePosition);
        }

        void Begin(int id, Vector2 position)
        {
            if (IgnoreAt != null && IgnoreAt(position)) return;
            fingerId = id;
            Anchor = Current = position;
            downTime = Time.unscaledTime;
            maxTravel = 0f;
            Dragging = true;
        }

        void Move(Vector2 position)
        {
            Current = position;
            maxTravel = Mathf.Max(maxTravel, (position - Anchor).magnitude);
        }

        void End(Vector2 position)
        {
            Move(position);
            bool tap = maxTravel < TapSlop && Time.unscaledTime - downTime < TapSeconds;
            Dragging = false;
            fingerId = -1;
            if (tap) Tapped?.Invoke(position);
        }
    }
}
