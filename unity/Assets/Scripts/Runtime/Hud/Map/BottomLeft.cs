using UnityEngine;

namespace Dogpatch.Map
{
    // The bottom-left corner is shared: the minimap owns the corner itself, and everything else that
    // lives there stacks in a column just right of it, bottom up in slot order, so nothing overlaps
    // in portrait or landscape. Each piece starts at Left(), lifts itself to Floor(slot) when a lower
    // piece is showing, and claims its top edge each frame it draws. Heights are pixels above the
    // safe area's bottom edge; a claim lasts one frame.
    public static class BottomLeft
    {
        public const int RoomCorner = 0;
        public const int AvatarCard = 1;
        public const int OperatorBar = 2;

        static readonly float[] tops = new float[3];
        static readonly int[] frames = { -10, -10, -10 };

        public static float Left(float left) => Minimap.Beside(left);

        // Lowest height (pixels above the safe bottom) a piece in this slot may start at.
        public static float Floor(int slot)
        {
            float floor = 0f;
            for (int i = 0; i < slot; i++)
                if (frames[i] >= Time.frameCount - 1) floor = Mathf.Max(floor, tops[i] + Mathf.Min(Screen.width, Screen.height) * 0.02f);
            return floor;
        }

        public static void Claim(int slot, float top)
        {
            tops[slot] = top;
            frames[slot] = Time.frameCount;
        }
    }
}
