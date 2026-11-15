using Dogpatch.Look;
using UnityEngine;

namespace Dogpatch.Map
{
    // Map looks Tim picks between (pause menu, MAP LOOK): the palette and line weights baked into
    // the vector map, plus the route, label and pin colours drawn over it. Arcade Clean (Tim's pick)
    // is the default; Classic is the original.
    public sealed class MapLook
    {
        const string PrefKey = "dogpatch.maplook.v2";

        public string Name;
        public MapGeometry.Palette Palette;
        public Color Bay;
        public Color Route;
        public Color RouteUnder;
        public Color LabelInk;
        public Color LabelHalo;
        public Color Mission;
        public bool Halftone;
        public Color Dots;
        public bool PopPins;

        static Color32 H(string hex) => SpeedDial.Hex(hex);

        public static readonly MapLook[] All =
        {
            new MapLook
            {
                Name = "ARCADE CLEAN",
                Palette = new MapGeometry.Palette
                {
                    Land = H("#EEF4FB"), Park = H("#B9EBCF"), Path = H("#E2EAF4"), Casing = H("#AEBBD4"), Street = H("#FFFFFF"),
                    Buildings = new[] { H("#D9DEEC"), H("#C9D0E2") },
                    Shadow = H("#B8C2D8"), ShadowOffset = new Vector2(1.6f, -1.6f), CasingExtra = 1.6f, StreetScale = 1.25f,
                },
                Bay = SpeedDial.Hex("#1C9BF2"), Route = SpeedDial.Hex("#FFCB1F"), RouteUnder = SpeedDial.Hex("#1C2347"),
                LabelInk = SpeedDial.Hex("#4F5872"), LabelHalo = SpeedDial.Hex("#EEF4FB"), Mission = SpeedDial.Hex("#FFCB1F"),
            },
            new MapLook
            {
                Name = "CLASSIC",
                Palette = new MapGeometry.Palette
                {
                    Land = H("#E6DDCB"), Park = H("#48C26A"), Path = H("#EEE7DA"), Casing = H("#8E929B"), Street = H("#A9ADB4"),
                    Buildings = new[] { H("#FF9C86") }, CasingExtra = 1.1f, StreetScale = 1f,
                },
                Bay = SpeedDial.Hex("#17B2D0"), Route = SpeedDial.Hex("#FFCB1F"), RouteUnder = SpeedDial.Hex("#1C2347"),
                LabelInk = SpeedDial.Hex("#1C2347"), LabelHalo = SpeedDial.Hex("#E6DDCB"), Mission = SpeedDial.Hex("#FFCB1F"),
            },
            new MapLook
            {
                Name = "POSTER",
                Palette = new MapGeometry.Palette
                {
                    Land = H("#FFF3DA"), Park = H("#8CE3BF"), Path = H("#FFE9C2"), Casing = H("#1C2347"), Street = H("#FFFFFF"),
                    Buildings = new[] { H("#FFE27A"), H("#FF9C86"), H("#BBA6F7"), H("#9ED7FF"), H("#FFB8A6") },
                    Shadow = H("#1C2347"), ShadowOffset = new Vector2(0.9f, -0.9f), CasingExtra = 2.4f, StreetScale = 1.15f,
                },
                Bay = SpeedDial.Hex("#17B2D0"), Route = SpeedDial.Hex("#FFCB1F"), RouteUnder = SpeedDial.Hex("#1C2347"),
                LabelInk = SpeedDial.Hex("#1C2347"), LabelHalo = SpeedDial.Hex("#FFFFFF"), Mission = SpeedDial.Hex("#FFCB1F"),
            },
            new MapLook
            {
                Name = "NEON",
                Palette = new MapGeometry.Palette
                {
                    Land = H("#121637"), Park = H("#0F3B3F"), Path = H("#252C5C"), Casing = new Color32(0x3B, 0xF0, 0xFF, 70), Street = H("#5CF4FF"),
                    Buildings = new[] { H("#1E2556"), H("#232B62"), H("#1A2050") }, CasingExtra = 3.6f, StreetScale = 0.55f,
                },
                Bay = SpeedDial.Hex("#060920"), Route = SpeedDial.Hex("#FF3EC8"), RouteUnder = new Color(1f, 0.24f, 0.78f, 0.35f),
                LabelInk = SpeedDial.Hex("#9FF8FF"), LabelHalo = SpeedDial.Hex("#060920"), Mission = SpeedDial.Hex("#FF3EC8"),
            },
            new MapLook
            {
                Name = "TOURIST",
                Palette = new MapGeometry.Palette
                {
                    Land = H("#F4E3C1"), Park = H("#9BCB6B"), Path = H("#EBD7AE"), Casing = H("#B78E5E"), Street = H("#FFF8E7"),
                    Buildings = new[] { H("#F2B880"), H("#F7CF9C"), H("#E9A46F"), H("#F5DDB5") },
                    Shadow = H("#9A6440"), ShadowOffset = new Vector2(2.6f, -2.6f), CasingExtra = 1.3f, StreetScale = 1.05f,
                },
                Bay = SpeedDial.Hex("#7CCBE0"), Route = SpeedDial.Hex("#E2603F"), RouteUnder = SpeedDial.Hex("#FFFFFF"),
                LabelInk = SpeedDial.Hex("#5A3A22"), LabelHalo = SpeedDial.Hex("#FFF4DE"), Mission = SpeedDial.Hex("#E2603F"),
                PopPins = true,
            },
            new MapLook
            {
                Name = "ZINE",
                Palette = new MapGeometry.Palette
                {
                    Land = H("#F3EBDD"), Park = H("#00A95C"), Path = H("#E9DCC6"), Casing = H("#3255A4"), Street = H("#F3EBDD"),
                    Buildings = new[] { H("#FF48B0"), H("#FF7AC6"), H("#3255A4") }, CasingExtra = 1.2f, StreetScale = 1f,
                },
                Bay = SpeedDial.Hex("#3255A4"), Route = SpeedDial.Hex("#FFE800"), RouteUnder = SpeedDial.Hex("#FF48B0"),
                LabelInk = SpeedDial.Hex("#FF48B0"), LabelHalo = SpeedDial.Hex("#F3EBDD"), Mission = SpeedDial.Hex("#FFE800"),
                Halftone = true, Dots = new Color(0.2f, 0.33f, 0.64f, 0.28f),
            },
        };

        static int? index;

        public static int Index
        {
            get => index ??= Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, All.Length - 1);
            set
            {
                index = Mathf.Clamp(value, 0, All.Length - 1);
                PlayerPrefs.SetInt(PrefKey, index.Value);
                PlayerPrefs.Save();
            }
        }

        public static MapLook Current => All[Index];
    }
}
