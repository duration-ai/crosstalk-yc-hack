using Dogpatch.Operator;
using Dogpatch.Operator.Voice;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The Operator call as a FaceTime call (CRO-78): while a call is open the voice card grows in place
    // into a small low-poly smartphone. Your front camera fills its screen, mirrored like a selfie, so
    // whoever watches the demo sees the person talking; Jay's name and the listening countdown sit at the top, a
    // waveform and the hang-up at the bottom, and your words caption the screen as Scribe hears them.
    // His DONE!/NOPE! and line caption it before it folds back into the card. No camera (denied,
    // missing, the editor) draws a placeholder in the same layout.
    public sealed partial class SlipstreamHud
    {
        const float VideoGrow = 0.26f;
        // Held in landscape, the front camera sits at the phone's short edge (left in LandscapeLeft,
        // right in LandscapeRight) while you look at the middle of the screen, so in the mirrored
        // frame your face lands about this fraction of the frame's width off centre, away from the
        // camera's side. The portrait phone screen keeps only ~40% of a landscape frame's width, so a
        // centred crop cuts the face; the crop slides this far towards it instead.
        const float LandscapeFaceShift = 0.15f;

        static WebCamTexture selfie;
        static bool selfieFront;
        static bool cameraAsked;

        float videoOpenedAt = -10f;
        int videoFrame = -10;
        float videoClosedAt = -10f;
        Rect lastPhone;
        Rect hangUpRect;
        bool hangUpVisible;

        // Runs the front camera only while a call is open, and releases it the moment the call ends.
        void VideoCamera()
        {
            bool want = Race != null && Race.Setup && (PreviewState ?? Race.Calls.State) != CallState.None;
            if (!want)
            {
                ReleaseCamera();
                return;
            }
            if (selfie != null) return;
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                if (!cameraAsked)
                {
                    cameraAsked = true;
                    Application.RequestUserAuthorization(UserAuthorization.WebCam);
                }
                return;
            }
            var devices = WebCamTexture.devices;
            if (devices.Length == 0) return;
            int pick = 0;
            for (int i = 0; i < devices.Length; i++)
                if (devices[i].isFrontFacing) { pick = i; break; }
            selfieFront = devices[pick].isFrontFacing;
            selfie = new WebCamTexture(devices[pick].name, 640, 480, 30);
            selfie.Play();
        }

        static void ReleaseCamera()
        {
            if (selfie == null) return;
            selfie.Stop();
            Destroy(selfie);
            selfie = null;
        }

        bool HangUpPress(Vector2 p)
        {
            if (!callVisible || !hangUpVisible || !Inflate(hangUpRect, 6f * s).Contains(p)) return false;
            hangUpVisible = false;
            Race.Calls.HangUp();
            return true;
        }

        Rect PhoneRect(bool readyShowing)
        {
            bool portrait = Gameplay.HudLayout.Portrait;
            float w = (portrait ? 152f : 134f) * s, h = (portrait ? 282f : 236f) * s;
            var slot = Gameplay.HudLayout.Call(96f * s, 64f * s, readyShowing);
            float x = Gameplay.HudLayout.CallFromLeft ? slot.x : slot.xMax - w;
            x = Mathf.Clamp(x, left, Mathf.Max(left, right - w));
            float y = Mathf.Max(top + 24f * s, slot.yMax - h);
            return new Rect(x, y, w, h);
        }

        static Rect Lerp(Rect a, Rect b, float t) => new Rect(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));

        static Rect Inset(Rect r, float by) => new Rect(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

        // A rect with its four corners cut at 45 degrees (the low-poly rounded corner).
        void Chamfer(Rect r, float c, Color color)
        {
            c = Mathf.Min(c, r.width * 0.5f, r.height * 0.5f);
            Panel(new Rect(r.x + c, r.y, r.width - 2f * c, r.height), color, 0f);
            Panel(new Rect(r.x, r.y + c, r.width, r.height - 2f * c), color, 0f);
            Corners(r, c, color);
        }

        // A diamond (a square turned 45 degrees) at each corner, centred inset by c: it fills the corner
        // triangles a chamfered shape keeps. Centred on the corners themselves (inset 0, side c * 1.41)
        // it cuts a picture's corners instead, in the bezel colour.
        void Corners(Rect r, float c, Color color, float inset = -1f)
        {
            if (inset < 0f) inset = c;
            float d = c * 1.4142f;
            var m = GUI.matrix;
            foreach (var at in new[] { new Vector2(r.x + inset, r.y + inset), new Vector2(r.xMax - inset, r.y + inset), new Vector2(r.x + inset, r.yMax - inset), new Vector2(r.xMax - inset, r.yMax - inset) })
            {
                GUIUtility.RotateAroundPivot(45f, at);
                Panel(new Rect(at.x - d * 0.5f, at.y - d * 0.5f, d, d), color, 0f);
                GUI.matrix = m;
            }
        }

        // The phone folding back into the card after the call: an ink silhouette for a beat.
        void VideoShrink(Rect plate)
        {
            float since = Time.time - videoClosedAt;
            if (since >= VideoGrow) return;
            float k = EaseOut(Mathf.Clamp01(since / VideoGrow));
            Chamfer(Lerp(lastPhone, plate, k), 12f * s * (1f - k), new Color(Ink.r, Ink.g, Ink.b, 1f - k * 0.5f));
        }

        // Draws the call phone; returns its rect (the hold-to-talk hit area).
        Rect VideoCall(Rect plate, CallState state, bool readyShowing, float now)
        {
            if (Time.frameCount - videoFrame > 2) videoOpenedAt = now;
            videoFrame = Time.frameCount;
            float grow = EaseOut(Mathf.Clamp01((now - videoOpenedAt) / VideoGrow));
            var phone = Lerp(plate, PhoneRect(readyShowing), grow);
            lastPhone = phone;
            videoClosedAt = now;
            hangUpVisible = false;

            var calls = Race.Calls;
            bool preview = PreviewState != null;
            bool talking = state == CallState.Talking, thinking = state == CallState.Waiting;
            float bezel = 4.5f * s;
            float corner = 8f * s * grow;

            Chamfer(new Rect(phone.x + 5f * s, phone.y + 5f * s, phone.width, phone.height), corner, Ink);
            if (talking)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * 6f);
                Chamfer(Inflate(phone, (2f + pulse * 3f) * s), corner + 2f * s, new Color(Pink.r, Pink.g, Pink.b, 0.3f + 0.45f * pulse));
            }
            Chamfer(phone, corner, Ink);
            if (grow < 0.6f) return phone;

            Panel(new Rect(phone.x - 2f * s, phone.y + phone.height * 0.2f, 2.5f * s, 18f * s), Ink, 0f);
            Panel(new Rect(phone.x - 2f * s, phone.y + phone.height * 0.2f + 22f * s, 2.5f * s, 18f * s), Ink, 0f);
            Panel(new Rect(phone.xMax - 0.5f * s, phone.y + phone.height * 0.26f, 2.5f * s, 26f * s), Ink, 0f);

            var screen = Inset(phone, bezel);
            Selfie(screen);
            float cut = Mathf.Min(2f * s, 2f * bezel - corner);
            if (cut > 0f) Corners(screen, cut, Ink, 0f);
            if (grow < 0.9f) return phone;

            float islandW = screen.width * 0.34f;
            Chamfer(new Rect(screen.center.x - islandW * 0.5f, screen.y + 4f * s, islandW, 9f * s), 4f * s, Ink);

            float level = preview ? 0.85f : Mathf.Clamp01(0.35f + Mathf.Sqrt(MicCapture.Level) * 1.4f);
            CallBar(screen, preview ? (talking ? 4f : -1f) : calls.ListenDeadline - now);
            var controls = Controls(screen, talking, level, now);

            if (state == CallState.Answered)
            {
                bool refused = preview ? PreviewRefused : answerRefused;
                string line = preview ? PreviewLine : calls.Line;
                Caption(screen, controls.y - 4f * s, line ?? "", refused ? "NOPE!" : "DONE!", refused);
            }
            else
            {
                string words = preview ? PreviewWords : thinking && !string.IsNullOrEmpty(calls.Heard) ? calls.Heard : calls.Partial;
                Caption(screen, controls.y - 4f * s, words, null, false, talking && Mathf.Repeat(now, 0.8f) < 0.55f);
            }
            return phone;
        }

        // The front camera filling the phone screen (cropped, never stretched), turned upright by its
        // videoRotationAngle and mirrored like a selfie, in AvatarCapture's order. No camera: an ink
        // screen with a person glyph.
        void Selfie(Rect r)
        {
            var cam = selfie;
            if (cam == null || !cam.isPlaying || cam.width <= 16)
            {
                Panel(r, Color.Lerp(Ink, White, 0.1f), 0f);
                var glyph = Color.Lerp(Ink, White, 0.3f);
                float side = r.width * 0.5f;
                var head = new Rect(r.center.x - side * 0.25f, r.center.y - side * 0.55f, side * 0.5f, side * 0.5f);
                Chamfer(head, side * 0.12f, glyph);
                Chamfer(new Rect(r.center.x - side * 0.48f, head.yMax + side * 0.08f, side * 0.96f, side * 0.5f), side * 0.16f, glyph);
                return;
            }
            int angle = ((cam.videoRotationAngle % 360) + 360) % 360;
            bool sideways = angle == 90 || angle == 270;
            float dw = sideways ? r.height : r.width, dh = sideways ? r.width : r.height;
            float texAspect = (float)cam.width / cam.height, drawAspect = dw / dh;
            var uv = texAspect > drawAspect
                ? new Rect((1f - drawAspect / texAspect) * 0.5f, 0f, drawAspect / texAspect, 1f)
                : new Rect(0f, (1f - texAspect / drawAspect) * 0.5f, 1f, texAspect / drawAspect);
            float slack = 1f - uv.width;
            if (!sideways && slack > 0f)
            {
                float towardFace = Screen.orientation == ScreenOrientation.LandscapeLeft ? 1f : Screen.orientation == ScreenOrientation.LandscapeRight ? -1f : 0f;
                float uPerScreenX = (angle == 180 ? -1f : 1f) * (selfieFront ? -1f : 1f);
                uv.x = Mathf.Clamp(uv.x + towardFace * uPerScreenX * LandscapeFaceShift, 0f, slack);
            }
            var center = r.center;
            var old = GUI.matrix;
            if (cam.videoVerticallyMirrored) GUIUtility.ScaleAroundPivot(new Vector2(1f, -1f), center);
            if (angle != 0) GUIUtility.RotateAroundPivot(angle, center);
            if (selfieFront) GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), center);
            GUI.DrawTextureWithTexCoords(new Rect(center.x - dw * 0.5f, center.y - dh * 0.5f, dw, dh), cam, uv);
            GUI.matrix = old;
        }

        // The top line, centred under the island: who you're talking to and, while an open call is
        // listening on its own, the seconds left before it sends what it heard. A held call has no
        // deadline (it sends when you let go), so that spot stays empty, as it does while Jay thinks.
        void CallBar(Rect screen, float secondsLeft)
        {
            float y = screen.y + 17f * s;
            Text(new Rect(screen.x, y, screen.width, 16f * s), "Jay", 14f, White, TextAnchor.MiddleCenter, 1.2f * s);
            if (secondsLeft <= 0f) return;
            Text(new Rect(screen.x, y + 16f * s, screen.width, 14f * s), Mathf.CeilToInt(secondsLeft) + "s", 12f, Sun, TextAnchor.MiddleCenter, 1.2f * s);
        }

        // The bottom row: a waveform that moves with your voice while the mic is open (flat and dim
        // while Jay thinks or answers) and the pink hang-up, live while the mic is open. Returns the
        // row's rect so the captions sit above it.
        Rect Controls(Rect screen, bool talking, float level, float now)
        {
            float b = 22f * s;
            var row = new Rect(screen.x, screen.yMax - b - 6f * s, screen.width, b);
            var end = new Rect(screen.xMax - b - 7f * s, row.y, b, b);
            Chamfer(end, 6f * s, Pink);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(135f, end.center);
            V3.HudIcons.Draw("call", Inset(end, 2f * s), White);
            GUI.matrix = m;

            const int bars = 9;
            float waveX = screen.x + 8f * s, waveW = end.x - 8f * s - waveX;
            float step = waveW / bars;
            var barColor = talking ? White : new Color(1f, 1f, 1f, 0.45f);
            for (int i = 0; i < bars; i++)
            {
                float k = talking ? Mathf.Clamp01(level * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(now * 11f + i * 1.3f)))) : 0f;
                float bh = Mathf.Lerp(3f * s, b, k);
                Panel(new Rect(waveX + i * step + step * 0.2f, row.center.y - bh * 0.5f, step * 0.55f, bh), barColor, 0f);
            }
            hangUpRect = end;
            hangUpVisible = talking && PreviewState == null;
            return row;
        }

        // Big captions over the bottom of the screen, white on a dark band; they run wider than the
        // phone (towards the middle of the screen) when the words need it, and keep the last two lines.
        void Caption(Rect screen, float bottomY, string words, string stamp, bool refused, bool cursor = false)
        {
            const float size = 16f;
            words = words ?? "";
            float stampW = stamp != null ? Measure(stamp, 14f).x + 16f * s : 0f;
            float pad = 7f * s;
            if (words.Length == 0 && stamp == null)
            {
                if (cursor) Panel(new Rect(screen.x + pad, bottomY - 22f * s, 5f * s, 16f * s), Pink, 0.1f);
                return;
            }
            var st = Style(size, TextAnchor.UpperLeft);
            st.wordWrap = true;
            float maxW = Mathf.Min(Mathf.Max(screen.width * 2.4f, 240f * s), right - left);
            float textW = Mathf.Clamp(Measure(words, size).x + 14f * s, screen.width - 2f * pad - stampW, maxW - 2f * pad - stampW);
            float lineH = st.lineHeight;
            string shown = words;
            var content = new GUIContent(shown);
            while (st.CalcHeight(content, textW) > lineH * 2.2f)
            {
                int cut = shown.IndexOf(' ', 1);
                if (cut < 0) break;
                shown = shown.Substring(cut + 1);
                content = new GUIContent("…" + shown);
            }
            float th = Mathf.Max(lineH, st.CalcHeight(content, textW));
            float bw = textW + 2f * pad + stampW, bh = th + 1.4f * pad;
            bool growLeft = screen.center.x > Screen.width * 0.5f;
            float bx = growLeft ? screen.xMax - bw : screen.x;
            bx = Mathf.Clamp(bx, left, Mathf.Max(left, right - bw));
            var bar = new Rect(bx, bottomY - bh, bw, bh);
            Panel(bar, new Color(Ink.r, Ink.g, Ink.b, 0.82f), 0f);
            float tx = bar.x + pad;
            if (stamp != null)
            {
                var sr = new Rect(tx, bar.y + pad * 0.7f, stampW - 6f * s, 22f * s);
                Panel(sr, refused ? Pink : Sun, 0.12f);
                Text(sr, stamp, 14f, refused ? White : Ink, TextAnchor.MiddleCenter);
                tx += stampW;
            }
            var box = new Rect(tx, bar.y + pad * 0.7f, textW, th);
            st.normal.textColor = TextColor(White, 1f);
            GUI.Label(box, content, st);
            if (cursor)
            {
                var at = st.GetCursorPixelPosition(box, content, content.text.Length);
                Panel(new Rect(at.x + 3f * s, at.y + 2f * s, 5f * s, lineH - 4f * s), Pink, 0.1f);
            }
            st.wordWrap = false;
        }
    }
}
