using System.Linq;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // HUD v3, Avatar A board screen 11: tapping the HUD portrait drops the racer card out of it,
    // the racer standing, the name on a white plate, a pencil to rename and a camera to retake.
    public sealed partial class AvatarCapture
    {
        Rect RacerCardRect()
        {
            float s = Scale;
            var p = PortraitRect();
            float w = 340f * s, h = 300f * s;
            return new Rect(p.x, p.y - 14f * s - h, w, h);
        }

        void DrawMenu()
        {
            float s = Scale;
            var card = RacerCardRect();
            BeginShapes();
            Para(new Rect(card.x - 4f * s, card.y - 4f * s, card.width + 8f * s, card.height + 8f * s), White);
            Para(card, Ink);
            Disc(new Vector2(card.x + 76f * s, card.y + 26f * s), 56f * s, 13f * s, Sunshine);
            Disc(new Vector2(card.x + 76f * s, card.y + 26f * s), 46f * s, 10f * s, Ink);
            var plate = new Rect(card.x + 150f * s, card.yMax - 86f * s, 170f * s, 54f * s);
            Para(plate, White);
            Line(new Vector2(card.x + 150f * s, card.y + 110f * s), new Vector2(card.x + 320f * s, card.y + 110f * s), 2f * s, new Color(1f, 1f, 1f, 0.3f));
            for (int i = 0; i < SuitColors.Length; i++)
                Para(new Rect(card.x + 152f * s + i * 30f * s, card.y + 126f * s, 22f * s, 22f * s), SuitColors[i]);
            foreach (var (id, rect) in Buttons().Where(b => b.id != "menu"))
            {
                bool down = pressed == id;
                if (id == "name") Plate(rect, Sunshine, White, down, r => PencilPictogram(r.center, r.height * 0.24f, Ink));
                else Plate(rect, Sunshine, White, down, r => CameraPictogram(r.center, r.height * 0.2f, Ink));
            }
            EndShapes();
            if (body != null)
            {
                float h = card.height - 30f * s, w = h * BodyWidth / BodyHeight;
                GUI.DrawTexture(new Rect(card.x + 76f * s - w / 2f, Screen.height - (card.y + 20f * s + h), w, h), body);
            }
            string name = Race.RaceDirector.Instance?.MyName ?? game?.Profile?.displayName ?? "";
            Label(plate, name.ToUpperInvariant(), Ink, 24f * s);
        }

        static void PencilPictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.5f);
            var a = c + new Vector2(-k * 1.1f, -k * 1.1f);
            var b = c + new Vector2(k * 1.1f, k * 1.1f);
            Line(a + new Vector2(k * 0.35f, k * 0.35f), b, t, stroke);
            Tri(a, a + new Vector2(k * 0.6f, k * 0.1f), a + new Vector2(k * 0.1f, k * 0.6f), stroke);
        }
    }
}
