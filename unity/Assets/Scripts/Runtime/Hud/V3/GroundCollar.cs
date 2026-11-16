using UnityEngine;

namespace Dogpatch.HudV3
{
    // HUD v3 board 5: the direction arrow leaves the middle of the screen and lies on the road as a
    // collar, a flat chevron around your car (4.2 m, so the chevron clears the
    // bonnet from the chase camera) or your feet (1.8 m), turned toward the next stretch of the route.
    public static class GroundCollar
    {
        public const float Radius = 3f;
        const float Lift = 0.06f;

        public static void Place(Transform chevron, Vector3 focus, float yaw, float shown, bool driving)
        {
            float radius = driving ? Radius * 1.4f : Radius * 0.6f;
            var ground = new Vector3(focus.x, focus.y + Lift, focus.z);
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            chevron.SetPositionAndRotation(ground + dir * radius, Quaternion.Euler(0f, yaw, 0f));
            float size = 0.55f * shown * (driving ? 1f : 0.7f);
            chevron.localScale = new Vector3(size, 0.25f * shown, size);
        }
    }
}
