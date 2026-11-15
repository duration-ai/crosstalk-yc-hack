using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.Glb;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Selfie-to-avatar. On first run (no avatar saved) a full-screen capture screen covers the
    // world: live camera (back by default, with a flip), a head-to-feet framing guide and one
    // shutter. The photo uploads to POST /api/avatar/photo, a short HOLD STILL plays while the
    // avatar is decided and downloaded, and the avatar then replaces the player model. A refused
    // photo (RETAKE, DECISION_UNAVAILABLE) asks for a retake. A small camera pictogram in the top
    // right reopens the screen later; a new photo replaces the avatar.
    // With no camera (simulator, Revyl, editor) the viewfinder shows a bundled test photo and the
    // shutter uploads that; only on this debug path does an offline upload fall back to the
    // in-memory canned avatar.
    public sealed class AvatarCapture : MonoBehaviour
    {
        const string AvatarKey = "dogpatch.avatar";
        const float Lean = 0.22f;

        static readonly Color Ink = new Color32(0x1C, 0x23, 0x47, 0xFF);
        static readonly Color Sunshine = new Color32(0xFF, 0xCB, 0x1F, 0xFF);
        static readonly Color White = Color.white;

        enum Stage { Closed, Viewfinder, Uploading }

        PlayerController player;
        GlbAsset clipSource;
        Material material;
        readonly ConvexAvatarClient client = new ConvexAvatarClient();
        DragInput drag;
        Stage stage = Stage.Closed;
        bool refused;
        bool hasAvatar;
        int wearing;

        WebCamDevice[] devices = new WebCamDevice[0];
        int deviceIndex = -1;
        WebCamTexture feed;
        Texture2D testPhoto;
        byte[] testPhotoBytes;
        Texture2D still;

        Material ui;
        Font font;
        GUIStyle label;
        string pressed;

        public static AvatarCapture Install(GameObject host, PlayerController player, GlbAsset clipSource, Material material)
        {
            var capture = host.AddComponent<AvatarCapture>();
            capture.player = player;
            capture.clipSource = clipSource;
            capture.material = material;
            return capture;
        }

        void Awake()
        {
            useGUILayout = false;
            ui = new Material(Resources.Load<Shader>("Shaders/HudColor")) { name = "AvatarCapture" };
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
        }

        async void Start()
        {
            drag = GetComponent<DragInput>();
            var saved = Parse(PlayerPrefs.GetString(AvatarKey, ""));
            if (saved != null)
            {
                hasAvatar = true;
                _ = WearAsync(saved);
            }

            string id = ConvexAvatarClient.StoredPlayerId;
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    var fresh = await client.GetAvatarAsync(id);
                    if (fresh != null && (saved == null || fresh.updatedAt != saved.updatedAt))
                    {
                        Save(fresh);
                        hasAvatar = true;
                        await WearAsync(fresh);
                    }
                }
                catch (BackendException e)
                {
                    Debug.LogWarning($"[Avatar] load failed {e.Code}: {e.Message}");
                    if (e.Code == BackendErrorCodes.NotFound) ConvexAvatarClient.ForgetPlayer();
                }
            }
            if (!hasAvatar) Open();
        }

        void OnDestroy() => StopFeed();

        static PlayerAvatar Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var avatar = JsonUtility.FromJson<PlayerAvatar>(json);
            return avatar == null || avatar.IsEmpty ? null : avatar;
        }

        static void Save(PlayerAvatar avatar)
        {
            PlayerPrefs.SetString(AvatarKey, JsonUtility.ToJson(avatar));
            PlayerPrefs.Save();
        }

        async Task<bool> WearAsync(PlayerAvatar avatar)
        {
            int version = ++wearing;
            GlbAsset glb = null;
            try { glb = await AvatarLoader.LoadAsync(avatar, clipSource); }
            catch (Exception e) { Debug.LogWarning("[Avatar] load failed: " + e.Message); }
            if (glb == null || version != wearing || player == null) return false;
            player.SetVisual(glb, material);
            Debug.Log($"[Avatar] wearing {avatar.@base} ({glb.Clips.Length} clips)");
            return true;
        }

        void Open()
        {
            stage = Stage.Viewfinder;
            refused = false;
            if (drag != null) drag.enabled = false;
            _ = StartFeedAsync();
        }

        void Close()
        {
            stage = Stage.Closed;
            StopFeed();
            if (drag != null) drag.enabled = true;
        }

        async Task StartFeedAsync()
        {
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                var asked = new TaskCompletionSource<bool>();
                Application.RequestUserAuthorization(UserAuthorization.WebCam).completed += _ => asked.TrySetResult(true);
                await asked.Task;
            }
            devices = Application.HasUserAuthorization(UserAuthorization.WebCam) ? WebCamTexture.devices : new WebCamDevice[0];
            if (stage != Stage.Viewfinder) return;
            if (devices.Length == 0)
            {
                if (testPhoto == null)
                {
                    testPhotoBytes = Resources.Load<TextAsset>("Avatar/test-photo").bytes;
                    testPhoto = new Texture2D(2, 2);
                    testPhoto.LoadImage(testPhotoBytes);
                }
                Debug.Log("[Avatar] no camera, showing the test photo");
                return;
            }
            if (deviceIndex < 0 || deviceIndex >= devices.Length)
            {
                deviceIndex = 0;
                for (int i = 0; i < devices.Length; i++)
                    if (!devices[i].isFrontFacing) { deviceIndex = i; break; }
            }
            StopFeed();
            feed = new WebCamTexture(devices[deviceIndex].name, 1920, 1080, 30);
            feed.Play();
        }

        void StopFeed()
        {
            if (feed == null) return;
            feed.Stop();
            Destroy(feed);
            feed = null;
        }

        void Flip()
        {
            if (devices.Length < 2) return;
            bool front = devices[deviceIndex].isFrontFacing;
            for (int k = 1; k < devices.Length; k++)
            {
                int i = (deviceIndex + k) % devices.Length;
                if (devices[i].isFrontFacing == front) continue;
                deviceIndex = i;
                break;
            }
            _ = StartFeedAsync();
        }

        void Shoot()
        {
            byte[] jpeg;
            bool debugPhoto = feed == null;
            if (feed != null && feed.isPlaying && feed.width > 16)
            {
                if (still != null && still != testPhoto) Destroy(still);
                still = Snapshot(feed);
                jpeg = still.EncodeToJPG(85);
            }
            else if (testPhotoBytes != null && feed == null)
            {
                still = testPhoto;
                jpeg = testPhotoBytes;
            }
            else return;
            stage = Stage.Uploading;
            StopFeed();
            _ = UploadAsync(jpeg, debugPhoto);
        }

        async Task UploadAsync(byte[] jpeg, bool debugPhoto)
        {
            PlayerAvatar avatar;
            try
            {
                avatar = await UploadForPlayerAsync(jpeg);
            }
            catch (BackendException e) when (e.Code == AvatarErrorCodes.Offline && debugPhoto)
            {
                Debug.LogWarning("[Avatar] offline, using the canned avatar: " + e.Message);
                avatar = InMemoryGameBackend.CannedAvatar(ConvexAvatarClient.StoredPlayerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar] photo refused: " + (e is BackendException b ? b.Code + " " : "") + e.Message);
                stage = Stage.Viewfinder;
                refused = true;
                _ = StartFeedAsync();
                return;
            }
            Save(avatar);
            hasAvatar = true;
            await WearAsync(avatar);
            Close();
        }

        async Task<PlayerAvatar> UploadForPlayerAsync(byte[] jpeg)
        {
            string id = await client.EnsurePlayerIdAsync();
            try
            {
                return (await client.UploadAvatarPhotoAsync(id, jpeg)).avatar;
            }
            catch (BackendException e) when (e.Code == BackendErrorCodes.NotFound)
            {
                ConvexAvatarClient.ForgetPlayer();
                id = await client.EnsurePlayerIdAsync();
                return (await client.UploadAvatarPhotoAsync(id, jpeg)).avatar;
            }
        }

        // The frame upright: the camera's vertical mirror undone, then its clockwise rotation.
        static Texture2D Snapshot(WebCamTexture cam)
        {
            int w = cam.width, h = cam.height;
            var src = cam.GetPixels32();
            int angle = ((cam.videoRotationAngle % 360) + 360) % 360;
            bool flip = cam.videoVerticallyMirrored;
            bool sideways = angle == 90 || angle == 270;
            int ow = sideways ? h : w, oh = sideways ? w : h;
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
            {
                int row = (flip ? h - 1 - y : y) * w;
                for (int x = 0; x < w; x++)
                {
                    int nx = x, ny = y;
                    if (angle == 90) { nx = y; ny = w - 1 - x; }
                    else if (angle == 180) { nx = w - 1 - x; ny = h - 1 - y; }
                    else if (angle == 270) { nx = h - 1 - y; ny = x; }
                    dst[ny * ow + nx] = src[row + x];
                }
            }
            var tex = new Texture2D(ow, oh, TextureFormat.RGB24, false);
            tex.SetPixels32(dst);
            tex.Apply();
            return tex;
        }

        float Scale => Mathf.Min(Screen.width, Screen.height) / 720f;

        // Button rects in screen pixels, origin bottom-left (the same space as touches).
        List<(string id, Rect rect)> Buttons()
        {
            float s = Scale;
            var safe = Screen.safeArea;
            var list = new List<(string, Rect)>();
            if (stage == Stage.Closed)
            {
                if (player == null || !player.gameObject.activeInHierarchy) return list;
                float w = 74f * s, h = 50f * s, m = 22f * s;
                list.Add(("retake", new Rect(safe.xMax - m - w - 14f * s - w, safe.yMax - m - h, w, h)));
                return list;
            }
            if (stage != Stage.Viewfinder) return list;
            float sw = 170f * s, sh = 96f * s;
            list.Add(("shutter", new Rect(safe.center.x - sw / 2f, safe.yMin + 40f * s, sw, sh)));
            if (devices.Length > 1) list.Add(("flip", new Rect(safe.xMax - 28f * s - 100f * s, safe.yMin + 52f * s, 100f * s, 72f * s)));
            if (hasAvatar) list.Add(("close", new Rect(safe.xMax - 22f * s - 74f * s, safe.yMax - 22f * s - 50f * s, 74f * s, 50f * s)));
            return list;
        }

        static string Hit(List<(string id, Rect rect)> buttons, Vector2 p)
        {
            foreach (var (id, rect) in buttons) if (rect.Contains(p)) return id;
            return null;
        }

        void Update()
        {
            Vector2 p;
            bool down, up;
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                p = t.position;
                down = t.phase == TouchPhase.Began;
                up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            }
            else
            {
                p = Input.mousePosition;
                down = Input.GetMouseButtonDown(0);
                up = Input.GetMouseButtonUp(0);
            }

            var buttons = Buttons();
            if (down) pressed = Hit(buttons, p);
            if (pressed == "retake" && down)
            {
                pressed = null;
                Open();
                return;
            }
            if (!up || pressed == null) return;
            string id = pressed;
            pressed = null;
            if (Hit(buttons, p) != id) return;
            if (id == "shutter") Shoot();
            else if (id == "flip") Flip();
            else if (id == "close") Close();
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = Scale;
            if (stage == Stage.Closed)
            {
                BeginShapes();
                foreach (var (id, rect) in Buttons()) Plate(rect, Ink, White, pressed == id, r => CameraPictogram(r.center, r.height * 0.36f, White));
                EndShapes();
                return;
            }

            DrawPhoto();

            BeginShapes();
            if (stage == Stage.Viewfinder) Silhouette(s);
            var tag = new Rect(Screen.safeArea.xMin + 22f * s, Screen.safeArea.yMax - 22f * s - 58f * s, 300f * s, 58f * s);
            Para(tag, refused ? Sunshine : Ink);
            if (stage == Stage.Uploading) Segments(new Rect(tag.x + 10f * s, tag.y - 26f * s, tag.width, 14f * s));
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "shutter") Plate(rect, Sunshine, Ink, down, r => CameraPictogram(r.center, r.height * 0.3f, down ? Sunshine : Ink));
                else if (id == "flip") Plate(rect, Ink, White, down, r => FlipPictogram(r.center, r.height * 0.26f, down ? Ink : White));
                else Plate(rect, Ink, White, down, r => ClosePictogram(r.center, r.height * 0.24f, down ? Ink : White));
            }
            EndShapes();

            string text = stage == Stage.Uploading ? "HOLD STILL" : refused ? "RETAKE" : "FULL BODY";
            Label(tag, text, refused ? Ink : Sunshine, s);
        }

        // Aspect-fill: the camera feed rotated upright (mirrored like a mirror for the front camera),
        // or the frozen shot while uploading, or the test photo.
        void DrawPhoto()
        {
            GUI.color = Ink;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Texture tex = stage == Stage.Uploading ? still : feed != null ? feed : testPhoto;
            if (tex == null || tex.width <= 16) return;

            int angle = 0;
            bool mirrorY = false, mirrorX = false;
            if (tex == feed)
            {
                angle = ((feed.videoRotationAngle % 360) + 360) % 360;
                mirrorY = feed.videoVerticallyMirrored;
                mirrorX = devices.Length > deviceIndex && deviceIndex >= 0 && devices[deviceIndex].isFrontFacing;
            }
            bool sideways = angle == 90 || angle == 270;
            float tw = sideways ? tex.height : tex.width, th = sideways ? tex.width : tex.height;
            float k = Mathf.Max(Screen.width / tw, Screen.height / th);
            var center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var old = GUI.matrix;
            if (mirrorY) GUIUtility.ScaleAroundPivot(new Vector2(1f, -1f), center);
            if (angle != 0) GUIUtility.RotateAroundPivot(angle, center);
            if (mirrorX) GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), center);
            float w = tex.width * k, h = tex.height * k;
            GUI.DrawTexture(new Rect(center.x - w / 2f, center.y - h / 2f, w, h), tex);
            GUI.matrix = old;
        }

        void Label(Rect rect, string text, Color color, float s)
        {
            if (label == null) label = new GUIStyle { font = font, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            label.fontSize = Mathf.RoundToInt(34f * s);
            label.normal.textColor = color;
            var r = new Rect(rect.x + rect.height * Lean * 0.5f + 18f * s, Screen.height - rect.yMax, rect.width, rect.height);
            GUI.Label(r, text, label);
        }

        void BeginShapes()
        {
            ui.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.TRIANGLES);
        }

        static void EndShapes()
        {
            GL.End();
            GL.PopMatrix();
        }

        // A flat button: ink outline, fill, pictogram. Pressed inverts the fill, shifts down-right
        // and shrinks slightly.
        void Plate(Rect rect, Color fill, Color pressedFill, bool down, Action<Rect> pictogram)
        {
            float s = Scale;
            if (down)
            {
                var c = rect.center + new Vector2(3f * s, -3f * s);
                rect = new Rect(c.x - rect.width * 0.48f, c.y - rect.height * 0.48f, rect.width * 0.96f, rect.height * 0.96f);
            }
            float o = 3f * s;
            Para(new Rect(rect.x - o, rect.y - o, rect.width + o * 2f, rect.height + o * 2f), down ? fill : Ink);
            Para(rect, down ? pressedFill : fill);
            pictogram(rect);
        }

        static void Para(Rect r, Color c)
        {
            float lean = r.height * Lean;
            var a = new Vector2(r.xMin, r.yMin);
            var b = new Vector2(r.xMax, r.yMin);
            var d = new Vector2(r.xMin + lean, r.yMax);
            var e = new Vector2(r.xMax + lean, r.yMax);
            Tri(a, b, e, c);
            Tri(a, e, d, c);
        }

        static void Tri(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            GL.Color(color);
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);
        }

        static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            var n = (b - a).normalized;
            var side = new Vector2(-n.y, n.x) * width * 0.5f;
            a -= n * width * 0.5f;
            b += n * width * 0.5f;
            Tri(a - side, a + side, b + side, c);
            Tri(a - side, b + side, b - side, c);
        }

        static void Ring(Vector2 center, float radius, float width, Color c)
        {
            const int segments = 28;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                float ri = radius - width * 0.5f, ro = radius + width * 0.5f;
                Tri(center + d0 * ri, center + d0 * ro, center + d1 * ro, c);
                Tri(center + d0 * ri, center + d1 * ro, center + d1 * ri, c);
            }
        }

        // Camera body with a lens, leaned forward like every pictogram.
        static void CameraPictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.2f);
            float lean = k * 0.18f;
            var bl = c + new Vector2(-k * 1.35f, -k * 0.85f);
            var br = c + new Vector2(k * 1.35f, -k * 0.85f);
            var tr = c + new Vector2(k * 1.35f + lean, k * 0.85f);
            var tl = c + new Vector2(-k * 1.35f + lean, k * 0.85f);
            Line(bl, br, t, stroke);
            Line(br, tr, t, stroke);
            Line(tr, tl, t, stroke);
            Line(tl, bl, t, stroke);
            Line(c + new Vector2(-k * 0.45f + lean, k * 0.85f), c + new Vector2(-k * 0.3f + lean, k * 1.2f), t, stroke);
            Line(c + new Vector2(-k * 0.3f + lean, k * 1.2f), c + new Vector2(k * 0.35f + lean, k * 1.2f), t, stroke);
            Line(c + new Vector2(k * 0.35f + lean, k * 1.2f), c + new Vector2(k * 0.5f + lean, k * 0.85f), t, stroke);
            Ring(c + new Vector2(lean * 0.5f, 0f), k * 0.5f, t, stroke);
        }

        static void FlipPictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.24f);
            Line(c + new Vector2(-k * 1.3f, k * 0.4f), c + new Vector2(k * 1.2f, k * 0.4f), t, stroke);
            Line(c + new Vector2(k * 1.2f, k * 0.4f), c + new Vector2(k * 0.6f, k * 0.95f), t, stroke);
            Line(c + new Vector2(k * 1.3f, -k * 0.4f), c + new Vector2(-k * 1.2f, -k * 0.4f), t, stroke);
            Line(c + new Vector2(-k * 1.2f, -k * 0.4f), c + new Vector2(-k * 0.6f, -k * 0.95f), t, stroke);
        }

        static void ClosePictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.3f);
            Line(c + new Vector2(-k, -k), c + new Vector2(k, k), t, stroke);
            Line(c + new Vector2(-k, k), c + new Vector2(k, -k), t, stroke);
        }

        // Twelve flat segments sweeping while the photo is read and the avatar downloads.
        static void Segments(Rect r)
        {
            float gap = r.width * 0.012f;
            float w = (r.width - gap * 11f) / 12f;
            int lit = Mathf.FloorToInt(Time.unscaledTime * 10f) % 13;
            for (int i = 0; i < 12; i++)
            {
                var seg = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                Para(new Rect(seg.x - 1.5f, seg.y - 1.5f, seg.width + 3f, seg.height + 3f), Ink);
                Para(seg, i < lit ? Sunshine : White);
            }
        }

        // Head-to-feet framing guide, drawn as a white stroke over an ink stroke.
        static readonly Vector2[] Outline =
        {
            new Vector2(0.035f, 0.135f), new Vector2(0.04f, 0.165f), new Vector2(0.13f, 0.19f), new Vector2(0.16f, 0.24f),
            new Vector2(0.175f, 0.47f), new Vector2(0.145f, 0.5f), new Vector2(0.125f, 0.3f), new Vector2(0.11f, 0.5f),
            new Vector2(0.105f, 0.965f), new Vector2(0.13f, 1f), new Vector2(0.025f, 1f), new Vector2(0.02f, 0.57f),
            new Vector2(0f, 0.55f),
        };

        static void Silhouette(float s)
        {
            float height = Screen.height * 0.7f;
            var top = new Vector2(Screen.width / 2f, Screen.height * 0.84f);
            Vector2 At(Vector2 p, float side) => top + new Vector2(p.x * side * height, -p.y * height);
            foreach (var (width, color) in new[] { (7f * s, Ink), (3.5f * s, White) })
            {
                Ring(top + new Vector2(0f, -0.07f * height), 0.065f * height, width, color);
                foreach (float side in new[] { -1f, 1f })
                    for (int i = 0; i < Outline.Length - 1; i++)
                        Line(At(Outline[i], side), At(Outline[i + 1], side), width, color);
            }
        }
    }
}
