using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // HUD v3, Avatar A board: the MAKE YOUR RACER ticket that slides out of the portrait on first
    // launch (tap opens the booth, ignoring it is a skip, remembered either way), and the NEW RACER
    // reveal, the new avatar idling on a turntable before the tick drops you back into play.
    public sealed partial class AvatarCapture
    {
        const string TicketKey = "dogpatch.racerTicket";
        const float TicketSeconds = 6f;
        const int TurntableWidth = 512, TurntableHeight = 768;

        float ticketAt = -1f;
        bool ticketDone;
        GameObject turntable;
        Camera turntableCam;
        RenderTexture turntableRt;
        float revealAt;

        bool TicketVisible => stage == Stage.Closed && PortraitVisible && !ticketDone && NameAsked
            && PlayerPrefs.GetInt(TicketKey, 0) == 0 && string.IsNullOrEmpty(PlayerPrefs.GetString(AvatarKey, ""));

        float TicketSlide
        {
            get
            {
                if (ticketAt < 0f) return 0f;
                float t = Time.unscaledTime - ticketAt;
                float o = Mathf.Clamp01(t / 0.35f), back = Mathf.Clamp01((t - TicketSeconds) / 0.35f);
                return Mathf.SmoothStep(0f, 1f, o) * (1f - Mathf.SmoothStep(0f, 1f, back));
            }
        }

        Rect TicketRect()
        {
            float s = Scale;
            var p = PortraitRect();
            float h = p.height * 0.62f;
            float w = TextWidth("MAKE YOUR RACER", 22f * s) + 96f * s;
            return new Rect(p.xMax - 10f * s, p.center.y - h / 2f, Mathf.Max(1f, w * TicketSlide), h);
        }

        void TapTicket()
        {
            ticketDone = true;
            PlayerPrefs.SetInt(TicketKey, 1);
            PlayerPrefs.Save();
            Open();
        }

        void DrawTicket()
        {
            if (TicketSlide < 0.02f) return;
            float s = Scale;
            var r = TicketRect();
            bool down = pressed == "ticket";
            BeginShapes();
            Para(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), Ink);
            Para(r, down ? White : Sunshine);
            if (TicketSlide > 0.9f) CameraPictogram(new Vector2(r.x + 40f * s, r.center.y), r.height * 0.2f, Ink);
            EndShapes();
            if (TicketSlide > 0.9f) Label(new Rect(r.x + 50f * s, r.y, r.width, r.height), "MAKE YOUR RACER", Ink, 22f * s);
            DrawPortrait();
        }

        void StartReveal()
        {
            stage = Stage.Reveal;
            revealAt = Time.unscaledTime;
            EndTurntable();
            turntable = worn.Instantiate(material);
            turntable.transform.SetPositionAndRotation(new Vector3(60f, 2000f, 0f), Quaternion.identity);
            foreach (var t in turntable.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PortraitLayer;
            var animator = turntable.GetComponent<GlbAnimator>();
            if (animator != null) animator.Play("Idle");
            var renderers = turntable.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                EndTurntable();
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float height = bounds.size.y;
            turntableCam = new GameObject("Avatar Turntable Camera").AddComponent<Camera>();
            turntableCam.enabled = false;
            turntableCam.cullingMask = 1 << PortraitLayer;
            turntableCam.clearFlags = CameraClearFlags.SolidColor;
            turntableCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            turntableCam.fieldOfView = 22f;
            turntableCam.nearClipPlane = 0.05f;
            turntableCam.farClipPlane = 60f;
            float distance = height * 0.6f / Mathf.Tan(11f * Mathf.Deg2Rad);
            var center = new Vector3(60f, 2000f + height * 0.5f, 0f);
            turntableCam.transform.position = center + new Vector3(0f, height * 0.08f, distance);
            turntableCam.transform.LookAt(center);
            turntableRt = new RenderTexture(TurntableWidth, TurntableHeight, 24, RenderTextureFormat.ARGB32) { name = "AvatarTurntable" };
            turntableCam.targetTexture = turntableRt;
        }

        void EndTurntable()
        {
            if (turntableCam != null) Destroy(turntableCam.gameObject);
            if (turntable != null) Destroy(turntable);
            if (turntableRt != null) turntableRt.Release();
            turntableCam = null;
            turntable = null;
            turntableRt = null;
        }

        void UpdateTurntable()
        {
            if (TicketVisible)
            {
                if (ticketAt < 0f) ticketAt = Time.unscaledTime;
                if (Time.unscaledTime - ticketAt > TicketSeconds + 0.4f)
                {
                    ticketDone = true;
                    PlayerPrefs.SetInt(TicketKey, 1);
                    PlayerPrefs.Save();
                }
            }
            if (stage != Stage.Reveal)
            {
                if (turntable != null) EndTurntable();
                return;
            }
            if (turntable == null || turntableCam == null) return;
            float t = Time.unscaledTime - revealAt;
            float spin = t < 1.2f ? Mathf.SmoothStep(0f, 360f, t / 1.2f) : 360f + Mathf.Sin((t - 1.2f) * 0.8f) * 25f;
            turntable.transform.rotation = Quaternion.Euler(0f, spin - 15f, 0f);
            turntableCam.Render();
        }

        void RevealButtons(System.Collections.Generic.List<(string, Rect)> list)
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            list.Add(("done", new Rect(safe.xMax - 28f * s - 210f * s, safe.yMin + 44f * s, 200f * s, 88f * s)));
        }

        // NEW RACER: the sunburst, the title, the new racer idling on the turntable, a big tick.
        void DrawReveal()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            bool tall = Screen.height > Screen.width;
            BeginShapes();
            Sunburst(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), Burst, BurstRay);
            EndShapes();

            float pop = Mathf.Clamp01((Time.unscaledTime - revealAt) / 0.3f);
            float h = (tall ? safe.height * 0.58f : safe.height * 0.74f) * (0.8f + 0.2f * pop);
            float w = h * TurntableWidth / TurntableHeight;
            var area = new Rect(safe.center.x - w / 2f, safe.yMin + (tall ? 200f : 30f) * s, w, h);
            var feet = new Vector2(area.center.x, area.yMin + h * 0.07f);
            BeginShapes();
            Disc(feet, w * 0.46f, w * 0.1f, Ink);
            Disc(feet, w * 0.4f, w * 0.08f, Sunshine);
            Disc(feet, w * 0.34f, w * 0.064f, Ink);
            Line(feet + new Vector2(-w * 0.58f, 0f), feet + new Vector2(-w * 0.5f, w * 0.05f), 5f * s, White);
            Line(feet + new Vector2(-w * 0.58f, 0f), feet + new Vector2(-w * 0.5f, -w * 0.05f), 5f * s, White);
            Line(feet + new Vector2(w * 0.58f, 0f), feet + new Vector2(w * 0.5f, w * 0.05f), 5f * s, White);
            Line(feet + new Vector2(w * 0.58f, 0f), feet + new Vector2(w * 0.5f, -w * 0.05f), 5f * s, White);
            EndShapes();
            if (turntableRt != null) GUI.DrawTexture(new Rect(area.x, Screen.height - area.yMax, w, h), turntableRt);

            if (big == null) big = new GUIStyle { font = font, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            big.alignment = TextAnchor.MiddleLeft;
            big.fontSize = Mathf.RoundToInt(64f * s);
            var title = new Rect(safe.xMin + 30f * s, Screen.height - safe.yMax + 30f * s, safe.width, 90f * s);
            big.normal.textColor = Ink;
            GUI.Label(new Rect(title.x + 4f * s, title.y + 4f * s, title.width, title.height), "NEW RACER", big);
            big.normal.textColor = Sunshine;
            GUI.Label(title, "NEW RACER", big);
            big.alignment = TextAnchor.MiddleCenter;
            float tw = Style(64f * s).CalcSize(new GUIContent("NEW RACER")).x;
            BeginShapes();
            for (int i = 0; i < 3; i++)
            {
                float y = Screen.height - title.center.y + (8f - i * 12f) * s;
                Line(new Vector2(title.x + tw + (20f + i * 10f) * s, y), new Vector2(title.x + tw + (70f + i * 10f) * s, y), 5f * s, White);
            }
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "done") Plate(rect, Sunshine, Ink, down, r => TickPictogram(r.center, r.height * 0.26f, down ? Sunshine : Ink));
            }
            EndShapes();
        }
    }
}
