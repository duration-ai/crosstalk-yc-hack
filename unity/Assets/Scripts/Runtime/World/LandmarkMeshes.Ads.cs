using UnityEngine;

namespace Dogpatch.World
{
    // Parody AI-startup ads in the deadpan style of the billboards on 101 and around SoMa: invented
    // brands, no real names or logos. Shared by the roadside unipoles and the Muni bus wraps.
    public struct AdCopy
    {
        public string Brand;
        public string Slogan;
        public string Panel;
        public string Ink;
        public string Accent;
        public int Mark;

        public AdCopy(string brand, string slogan, string panel, string ink, string accent, int mark)
        {
            Brand = brand;
            Slogan = slogan;
            Panel = panel;
            Ink = ink;
            Accent = accent;
            Mark = mark;
        }
    }

    public static partial class LandmarkMeshes
    {
        public static readonly AdCopy[] Ads =
        {
            new AdCopy("LOOMWORK", "STOP HIRING HUMANS", "#F4F1EA", "#111317", "#FF5A1F", 0),
            new AdCopy("TENSORBRO", "YOUR COFOUNDER IS A GPU", "#111317", "#F4F1EA", "#7CFF4F", 1),
            new AdCopy("GENERALLY", "AGI BY Q3", "#FFFFFF", "#111317", "#3B5BFF", 2),
            new AdCopy("HOTFIX", "SHIP IT. WE'LL FIX IT IN PROD.", "#FF5A1F", "#111317", "#FFFFFF", 3),
            new AdCopy("EVALSY", "VIBES ARE NOT AN EVAL", "#E9E4FF", "#2A1B6E", "#6C4BFF", 0),
            new AdCopy("CLOSEBOT", "YOUR SDR NEVER SLEEPS. OR ASKS FOR EQUITY.", "#0E2A1F", "#E8FFE9", "#3DDC84", 1),
            new AdCopy("NAPKIN", "WE RAISED 40M ON A NAPKIN", "#FFF3DA", "#1C2347", "#E2603F", 2),
            new AdCopy("FOUNDERMODE", "SLEEP IS A LEGACY SYSTEM", "#111317", "#FFCB1F", "#FFCB1F", 3),
            new AdCopy("COMPLIANZA", "SOC 2 WHILE YOU SLEEP", "#DDEBFF", "#0B1F4D", "#2F6BFF", 0),
            new AdCopy("TOKENWISE", "FEWER TOKENS. MORE FEELINGS.", "#FFE1EC", "#3A0B22", "#FF4F8B", 1),
            new AdCopy("MERGELY", "LGTM, SAID THE AGENT", "#FFFFFF", "#111317", "#111317", 2),
            new AdCopy("SERIES ANYTHING", "PRE-REVENUE. POST-HYPE.", "#1C2347", "#FFFFFF", "#FF5A1F", 3),
            new AdCopy("HUMANISH", "STOP FIRING HUMANS", "#FFCB1F", "#111317", "#111317", 0),
            new AdCopy("CONTEXTFUL", "1 BILLION TOKENS OF CONTEXT. ZERO NUANCE.", "#F4F1EA", "#111317", "#00A3A3", 1),
            new AdCopy("AGENTIK", "HIRE 100 AGENTS. KEEP ALL THE MANAGERS.", "#111317", "#FFFFFF", "#FF3B3B", 2),
            new AdCopy("INFERNO", "INFERENCE SO FAST IT'S WRONG SOONER", "#2B0A0A", "#FFD7C2", "#FF5A1F", 3),
            new AdCopy("PROMPTLY", "YOUR PROMPTS, BUT ENTERPRISE", "#EAF6F0", "#0E3B2E", "#18A36B", 0),
            new AdCopy("DEPLOYD", "IT WORKS ON MY CLUSTER", "#111317", "#7CFF4F", "#7CFF4F", 1),
            new AdCopy("LATENT", "WE'RE HIRING. THE AI IS DOING THE HIRING.", "#FFFFFF", "#3B5BFF", "#3B5BFF", 2),
            new AdCopy("RUNWAYLESS", "18 MONTHS OF RUNWAY. 3 WEEKS OF HYPE.", "#FFF3DA", "#111317", "#E2603F", 3),
        };

        // One ad face: the slogan big over the panel, with a brand band along the bottom holding a
        // simple mark and the brand name. center is the face's centre, normal points at the viewer.
        public static void AdFace(MeshBuilder mb, Vector3 center, Vector3 right, Vector3 normal, float width, float height, AdCopy ad, float glow)
        {
            var up = Vector3.Cross(normal, right).normalized;
            if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
            Color Lit(string hex)
            {
                var c = Hex(hex);
                c.a = 1f - glow;
                return c;
            }
            var panel = Lit(ad.Panel);
            var ink = Lit(ad.Ink);
            var accent = Lit(ad.Accent);
            Vector3 Q(float u, float v, float lift) => center + right * u + up * v + normal * lift;
            mb.Quad(Q(-width * 0.5f, -height * 0.5f, 0f), Q(width * 0.5f, -height * 0.5f, 0f), Q(width * 0.5f, height * 0.5f, 0f), Q(-width * 0.5f, height * 0.5f, 0f), panel, normal);

            float margin = Mathf.Max(0.15f, Mathf.Min(width, height) * 0.07f);
            float band = height * 0.24f;
            float bandTop = -height * 0.5f + margin + band;
            float sloganH = height * 0.5f - margin - (bandTop + margin * 0.6f);
            TextBlock(mb, Q(0f, bandTop + margin * 0.6f + sloganH * 0.5f, 0.03f), right, normal, width - margin * 2f, sloganH, ad.Slogan, ink);

            float rule = Mathf.Max(0.03f, height * 0.012f);
            mb.Quad(Q(-width * 0.5f + margin, bandTop, 0.02f), Q(width * 0.5f - margin, bandTop, 0.02f), Q(width * 0.5f - margin, bandTop + rule, 0.02f), Q(-width * 0.5f + margin, bandTop + rule, 0.02f), accent, normal);

            float bandMid = bandTop - band * 0.5f - rule;
            float markS = band * 0.62f;
            float brandW = Mathf.Min(width * 0.5f, ad.Brand.Length * markS * 0.75f);
            float markU = -(brandW + markS * 1.3f) * 0.5f + markS * 0.5f;
            Mark(mb, Q(markU, bandMid, 0.03f), right, up, normal, markS, ad.Mark, accent, panel);
            TextBlock(mb, Q(markU + markS * 0.8f + brandW * 0.5f, bandMid, 0.03f), right, normal, brandW, markS, ad.Brand, ink);
        }

        // Four made-up logo marks: a square with a notch, a ring, a triangle and stacked bars.
        static void Mark(MeshBuilder mb, Vector3 c, Vector3 right, Vector3 up, Vector3 normal, float s, int kind, Color color, Color cut)
        {
            float h = s * 0.5f;
            Vector3 P(float u, float v, float lift = 0f) => c + right * u + up * v + normal * lift;
            switch (kind % 4)
            {
                case 0:
                    mb.Quad(P(-h, -h), P(h, -h), P(h, h), P(-h, h), color, normal);
                    mb.Quad(P(0f, 0f, 0.01f), P(h, 0f, 0.01f), P(h, h, 0.01f), P(0f, h, 0.01f), cut, normal);
                    break;
                case 1:
                    const int Sides = 10;
                    for (int i = 0; i < Sides; i++)
                    {
                        float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                        mb.Quad(P(Mathf.Cos(a0) * h * 0.5f, Mathf.Sin(a0) * h * 0.5f), P(Mathf.Cos(a0) * h, Mathf.Sin(a0) * h),
                            P(Mathf.Cos(a1) * h, Mathf.Sin(a1) * h), P(Mathf.Cos(a1) * h * 0.5f, Mathf.Sin(a1) * h * 0.5f), color, normal);
                    }
                    break;
                case 2:
                    mb.Triangle(P(-h, -h), P(h, -h), P(0f, h), color, normal);
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        float v0 = -h + i * s * 0.38f;
                        float w = h * (1f - i * 0.25f);
                        mb.Quad(P(-h, v0), P(-h + 2f * w, v0), P(-h + 2f * w, v0 + s * 0.24f), P(-h, v0 + s * 0.24f), color, normal);
                    }
                    break;
            }
        }

        // A roadside unipole: one steel column, a catwalk, and a back-to-back head carrying a different
        // ad on each face. baseCenter is the foot of the column and the front faces headingDeg. The head
        // sits headOffset along the front face's right from the column.
        public static void Unipole(MeshBuilder mb, Vector3 baseCenter, float headingDeg, float width, float height, float poleHeight, float headOffset, AdCopy front, AdCopy back)
        {
            const float Depth = 0.9f;
            const float Border = 0.3f;
            float rad = headingDeg * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            var right = new Vector3(-Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            var rot = Yaw(headingDeg);
            var steel = Hex("#5E6878");
            var steelDark = Hex("#3E4552");
            var head = baseCenter + right * headOffset + Vector3.up * (poleHeight + height * 0.5f);

            mb.Box(baseCenter + Vector3.up * ((poleHeight + height * 0.5f) * 0.5f), new Vector3(0.7f, poleHeight + height * 0.5f, 0.7f), rot, steel);
            mb.Box(baseCenter + Vector3.up * 0.3f, new Vector3(2.0f, 0.6f, 2.0f), rot, ConcreteMid);
            mb.Box(head, new Vector3(width + Border * 2f, height + Border * 2f, Depth), rot, steelDark);

            float deckY = poleHeight - Border - 0.08f;
            foreach (float side in new[] { 1f, -1f })
            {
                var deck = baseCenter + right * headOffset + Vector3.up * deckY + fwd * side * (Depth * 0.5f + 0.5f);
                mb.Box(deck, new Vector3(width, 0.12f, 1.0f), rot, steelDark);
                mb.Box(deck + Vector3.up * 0.9f + fwd * side * 0.45f, new Vector3(width, 0.06f, 0.06f), rot, steelDark);
            }

            for (int i = -1; i <= 1; i++)
                foreach (float side in new[] { 1f, -1f })
                {
                    var arm = head + Vector3.up * (height * 0.5f + Border + 0.15f) + right * (i * width * 0.33f) + fwd * side * (Depth * 0.5f + 0.6f);
                    mb.Box(arm, new Vector3(0.2f, 0.2f, 1.2f), rot, steelDark);
                    mb.Box(arm + fwd * side * 0.5f, new Vector3(0.5f, 0.2f, 0.4f), rot, Lemon);
                }

            AdFace(mb, head + fwd * (Depth * 0.5f + 0.01f), right, fwd, width, height, front, 0.15f);
            AdFace(mb, head - fwd * (Depth * 0.5f + 0.01f), -right, -fwd, width, height, back, 0.15f);
        }
    }
}
