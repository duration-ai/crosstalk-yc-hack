using UnityEngine;

namespace Dogpatch.Slipstream
{
    // HUD v3 board 4: three start lights under the grid digit. Each is a 40x14 pt bar on a 46 pt
    // pitch, ink at 45% while off and white once its second has come; all three turn sunshine on GO.
    public sealed partial class SlipstreamHud
    {
        void StartLights(float centerX, float y, int lit, bool go, float alpha)
        {
            float p = HudV3.V3Draw.P;
            float w = 40f * p, h = 14f * p, pitch = 46f * p;
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(centerX + (i - 1) * pitch - w * 0.5f, y, w, h);
                var c = go ? Sun : i < lit ? White : new Color(Ink.r, Ink.g, Ink.b, 0.45f);
                c.a *= alpha;
                Panel(Inflate(r, 2f * p), new Color(Ink.r, Ink.g, Ink.b, alpha));
                Panel(r, c);
            }
        }
    }
}
