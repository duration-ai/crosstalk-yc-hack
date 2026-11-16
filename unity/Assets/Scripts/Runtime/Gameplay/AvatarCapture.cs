using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dogpatch.Backend;
using Dogpatch.Glb;
using UnityEngine;
using Avatar = Dogpatch.Backend.Avatar;

namespace Dogpatch.Gameplay
{
    // Selfie-to-avatar, never in the way: the player starts as the stock commuter. In free roam a
    // small round portrait of the player's current avatar sits top left (rendered once from the
    // model, re-rendered after a change); tapping it opens the camera. The pause menu carries an
    // AVATAR chip that opens the camera any time. The camera screen shows the live feed with a head-to-feet guide, a shutter, a skip
    // and a flip; the photo goes to IGameBackend.AnalyzeAvatarPhotoAsync under the game's own
    // player id, SUITING UP plays while the server answers and the GLB loads, and the avatar then
    // replaces the player model. RETAKE shows NO ONE IN SHOT, anything else TRY AGAIN, both with
    // retake and skip. Tapping the portrait offers RETAKE PHOTO and CHANGE NAME. On first launch a
    // YOUR NAME prompt asks for the name other racers see; skipping keeps the random "Racer NN".
    //   -dogpatch-no-avatar-prompt / DOGPATCH_NO_AVATAR_PROMPT, and batchmode: no portrait button.
    //   -dogpatch-avatar-test-photo / DOGPATCH_AVATAR_TEST_PHOTO: the bundled test photo stands in
    //   for the camera (also used automatically on a device with no camera, e.g. the simulator).
    public sealed class AvatarCapture : MonoBehaviour
    {
        const string AvatarKey = "dogpatch.avatar";
        const string NameAskedKey = "dogpatch.nameAsked";
        const int MaxName = 16;
        const int PortraitSize = 192;
        const int PortraitLayer = 31;
        const int MaxPhotoSide = 1024;
        const float Lean = 0.22f;

        static readonly Color Ink = new Color32(0x1C, 0x23, 0x47, 0xFF);
        static readonly Color Sunshine = new Color32(0xFF, 0xCB, 0x1F, 0xFF);
        static readonly Color White = Color.white;
        static readonly Color PortraitSky = new Color32(0x9E, 0xD7, 0xFF, 0xFF);

        enum Stage { Closed, Menu, Name, Viewfinder, Uploading, Refused, Failed }

        GameController game;
        Hud hud;
        PlayerController player;
        GlbAsset clipSource;
        Material material;
        DragInput drag;
        Slipstream.SlipstreamHud raceHud;
        Func<Vector2, bool> ignore;
        Stage stage = Stage.Closed;
        string failure = "";
        GlbAsset worn;
        Texture2D portrait;
        bool portraitDirty = true;
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
        string typed = "";
        string placeholder = "";
        TouchScreenKeyboard keyboard;

        public static AvatarCapture Install(GameObject host, PlayerController player, GlbAsset clipSource, Material material)
        {
            var capture = host.AddComponent<AvatarCapture>();
            capture.player = player;
            capture.clipSource = clipSource;
            capture.material = material;
            return capture;
        }

        static bool Flag(string arg, string env) =>
            Environment.GetCommandLineArgs().Contains(arg) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env));

        static bool ButtonSuppressed => Application.isBatchMode || Flag("-dogpatch-no-avatar-prompt", "DOGPATCH_NO_AVATAR_PROMPT");

        static bool ForceTestPhoto => Flag("-dogpatch-avatar-test-photo", "DOGPATCH_AVATAR_TEST_PHOTO");

        bool FullScreen => stage == Stage.Name || stage == Stage.Viewfinder || stage == Stage.Uploading || stage == Stage.Refused || stage == Stage.Failed;

        string PlayerId => game != null && game.Profile != null ? game.Profile.id : null;

        void Awake()
        {
            useGUILayout = false;
            ui = new Material(Resources.Load<Shader>("Shaders/HudColor")) { name = "AvatarCapture" };
            font = Resources.Load<Font>("Fonts/Exo2-BlackItalic");
        }

        async void Start()
        {
            game = GetComponent<GameController>();
            hud = GetComponent<Hud>();
            drag = GetComponent<DragInput>();
            worn = clipSource;

            var saved = Parse(PlayerPrefs.GetString(AvatarKey, ""));
            if (saved != null) _ = WearAsync(saved);
            if (game != null && game.Backend != null && PlayerId != null)
            {
                try
                {
                    var fresh = await game.Backend.GetAvatarAsync(PlayerId);
                    if (fresh != null && (saved == null || fresh.updatedAt != saved.updatedAt || fresh.glbUrl != saved.glbUrl))
                    {
                        Save(fresh);
                        saved = fresh;
                        _ = WearAsync(fresh);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Avatar] load failed: " + (e is BackendException b ? b.Code + " " : "") + e.Message);
                }
            }
        }

        void OnDestroy()
        {
            StopFeed();
            if (portrait != null) Destroy(portrait);
        }

        void OnEnable() => ScreenShape.Changed += CancelPress;

        void OnDisable() => ScreenShape.Changed -= CancelPress;

        void CancelPress() => pressed = null;

        static Avatar Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var avatar = JsonUtility.FromJson<Avatar>(json);
            return avatar == null || avatar.IsEmpty ? null : avatar;
        }

        static void Save(Avatar avatar)
        {
            PlayerPrefs.SetString(AvatarKey, JsonUtility.ToJson(avatar));
            PlayerPrefs.Save();
        }

        async Task<bool> WearAsync(Avatar avatar)
        {
            int version = ++wearing;
            GlbAsset glb = null;
            try { glb = await AvatarLoader.LoadAsync(avatar, clipSource); }
            catch (Exception e) { Debug.LogWarning("[Avatar] model load failed: " + e.Message); }
            if (glb == null || version != wearing || player == null) return false;
            player.SetVisual(glb, material);
            worn = glb;
            portraitDirty = true;
            Debug.Log($"[Avatar] wearing {avatar.@base} ({glb.Clips.Length} clips, {avatar.recolor?.Length ?? 0} recolours)");
            return true;
        }

        // The race HUD (lobby roster, Ready) draws above this screen and takes taps under its
        // buttons, so it sits out while the camera screen is up.
        void Open()
        {
            stage = Stage.Viewfinder;
            if (drag != null) drag.enabled = false;
            if (raceHud == null) raceHud = FindAnyObjectByType<Slipstream.SlipstreamHud>();
            if (raceHud != null) raceHud.enabled = false;
            _ = StartFeedAsync();
        }

        void Close()
        {
            stage = Stage.Closed;
            StopFeed();
            if (drag != null) drag.enabled = true;
            if (raceHud != null) raceHud.enabled = true;
        }

        // The name prompt: the iOS keyboard where there is one, typed keys on a desktop.
        internal void OpenName()
        {
            stage = Stage.Name;
            typed = "";
            var current = Race.RaceDirector.Instance?.MyName;
            placeholder = string.IsNullOrWhiteSpace(current) ? "Racer " + UnityEngine.Random.Range(10, 100) : current;
            if (drag != null) drag.enabled = false;
            if (raceHud == null) raceHud = FindAnyObjectByType<Slipstream.SlipstreamHud>();
            if (raceHud != null) raceHud.enabled = false;
            ShowKeyboard();
        }

        void ShowKeyboard()
        {
            if (!TouchScreenKeyboard.isSupported) return;
            TouchScreenKeyboard.hideInput = true;
            keyboard = TouchScreenKeyboard.Open(typed, TouchScreenKeyboardType.Default, false, false, false, false, placeholder, MaxName);
        }

        // OK with an empty field, or the skip button, keeps the current name.
        internal void CloseName(bool save)
        {
            string name = typed.Trim();
            if (keyboard != null) keyboard.active = false;
            keyboard = null;
            PlayerPrefs.SetInt(NameAskedKey, 1);
            PlayerPrefs.Save();
            Close();
            if (!save || name.Length == 0) return;
            if (game != null) game.Rename(name);
            Race.RaceDirector.Instance?.Rename(name);
            Debug.Log("[Avatar] name set: " + name);
        }

        internal void Type(string text) => typed = text.Length > MaxName ? text.Substring(0, MaxName) : text;

        void UpdateName()
        {
            if (keyboard != null)
            {
                Type(keyboard.text ?? "");
                if (keyboard.status == TouchScreenKeyboard.Status.Done) CloseName(true);
                return;
            }
            foreach (char c in Input.inputString)
            {
                if (c == '\b') { if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1); }
                else if (c == '\n' || c == '\r') { CloseName(true); return; }
                else if (!char.IsControl(c)) Type(typed + c);
            }
        }

        bool NameAsked => PlayerPrefs.GetInt(NameAskedKey, 0) == 1 || ButtonSuppressed
            || Environment.GetCommandLineArgs().Contains("-name");

        void Fail(string text)
        {
            StopFeed();
            failure = text;
            stage = Stage.Failed;
        }

        async Task StartFeedAsync()
        {
            if (ForceTestPhoto)
            {
                ShowTestPhoto();
                return;
            }
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                var asked = new TaskCompletionSource<bool>();
                Application.RequestUserAuthorization(UserAuthorization.WebCam).completed += _ => asked.TrySetResult(true);
                await asked.Task;
            }
            if (stage != Stage.Viewfinder) return;
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Fail("CAMERA OFF");
                return;
            }
            devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                ShowTestPhoto();
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

        void ShowTestPhoto()
        {
            if (testPhoto == null)
            {
                testPhotoBytes = Resources.Load<TextAsset>("Avatar/test-photo").bytes;
                testPhoto = new Texture2D(2, 2);
                testPhoto.LoadImage(testPhotoBytes);
            }
            Debug.Log("[Avatar] no camera, the test photo stands in");
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
            if (feed == null || devices.Length < 2) return;
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
            if (feed != null)
            {
                if (!feed.isPlaying || feed.width <= 16) return;
                if (still != null && still != testPhoto) Destroy(still);
                still = Snapshot(feed, MaxPhotoSide);
                jpeg = still.EncodeToJPG(85);
            }
            else if (testPhotoBytes != null)
            {
                still = testPhoto;
                jpeg = testPhotoBytes;
            }
            else return;
            StopFeed();
            _ = UploadAsync(jpeg);
        }

        async Task UploadAsync(byte[] jpeg)
        {
            stage = Stage.Uploading;
            AvatarResult result;
            try
            {
                if (game == null || game.Backend == null || PlayerId == null) throw new BackendException(BackendErrorCodes.Internal, "no player yet");
                result = await game.Backend.AnalyzeAvatarPhotoAsync(PlayerId, jpeg);
            }
            catch (BackendException e) when (e.Code == AvatarErrorCodes.Retake)
            {
                Debug.Log("[Avatar] retake: " + e.Message);
                stage = Stage.Refused;
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Avatar] photo failed: " + (e is BackendException b ? b.Code + " " : "") + e.Message);
                Fail("TRY AGAIN");
                return;
            }
            var d = result.decision;
            Debug.Log($"[Avatar] decided by {d?.provider} in {d?.latencyMs}ms realPerson={d?.realPerson} costume={d?.costume}: {result.avatar.@base} {result.avatar.glbUrl}");
            Save(result.avatar);
            if (!await WearAsync(result.avatar))
            {
                Fail("TRY AGAIN");
                return;
            }
            Close();
        }

        // The frame upright and at most maxSide pixels on its long side: the camera's vertical
        // mirror undone, then its clockwise videoRotationAngle applied. The front camera's buffer
        // is not mirrored (only the preview is, like a mirror), so the upload is never flipped
        // sideways.
        static Texture2D Snapshot(WebCamTexture cam, int maxSide)
        {
            int w = cam.width, h = cam.height;
            var src = cam.GetPixels32();
            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(w, h) / (float)maxSide));
            int sw = w / step, sh = h / step;
            int angle = ((cam.videoRotationAngle % 360) + 360) % 360;
            bool flip = cam.videoVerticallyMirrored;
            bool sideways = angle == 90 || angle == 270;
            int ow = sideways ? sh : sw, oh = sideways ? sw : sh;
            var dst = new Color32[ow * oh];
            for (int y = 0; y < sh; y++)
            {
                int sy = y * step;
                int row = (flip ? h - 1 - sy : sy) * w;
                for (int x = 0; x < sw; x++)
                {
                    int nx = x, ny = y;
                    if (angle == 90) { nx = y; ny = sw - 1 - x; }
                    else if (angle == 180) { nx = sw - 1 - x; ny = sh - 1 - y; }
                    else if (angle == 270) { nx = sh - 1 - y; ny = x; }
                    dst[ny * ow + nx] = src[row + x * step];
                }
            }
            var tex = new Texture2D(ow, oh, TextureFormat.RGB24, false);
            tex.SetPixels32(dst);
            tex.Apply();
            return tex;
        }

        float Scale => Mathf.Min(Screen.width, Screen.height) / 720f;

        bool PauseChipVisible => stage == Stage.Closed && hud != null && hud.Paused;

        // Free roam only: never in the mission lobby, on the grid, mid-race or on the results.
        bool PortraitVisible => (stage == Stage.Closed || stage == Stage.Menu) && portrait != null && !ButtonSuppressed && (hud == null || !hud.Paused)
            && !HudLayout.LobbyShowing && !(Race.RaceDirector.Instance is Race.RaceDirector race && (race.Gridded || race.Locked));

        // The portrait button in screen pixels, origin bottom-left.
        static Rect PortraitRect()
        {
            var r = HudLayout.AvatarButton;
            return new Rect(r.x, Screen.height - r.yMax, r.width, r.height);
        }

        static Color PlayerColor =>
            Slipstream.SlipstreamHud.SlotColor(Race.RaceDirector.Instance?.Me?.color);

        // The label panel in the top-left corner, sized to its text; origin bottom-left.
        Rect TagRect(string text)
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            float h = 58f * s;
            float w = TextWidth(text, 34f * s) + h * Lean + 44f * s;
            return new Rect(safe.xMin + 22f * s, safe.yMax - 22f * s - h, w, h);
        }


        // Pause menu chip, a row under the HUD's time and weather chips.
        Rect PauseChipRect()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            float w = 220f * s, h = 40f * s, top = 92f * s + 3f * (40f + 12f) * s;
            return new Rect(safe.xMin + 24f * s - 0.5f * h * Lean, safe.yMax - top - h, w, h);
        }

        // Button rects in screen pixels, origin bottom-left (the same space as touches).
        List<(string id, Rect rect)> Buttons()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            var list = new List<(string, Rect)>();
            switch (stage)
            {
                case Stage.Closed:
                    if (PauseChipVisible) list.Add(("open", PauseChipRect()));
                    if (PortraitVisible) list.Add(("menu", PortraitRect()));
                    break;
                case Stage.Menu:
                    if (!PortraitVisible) break;
                    var portraitRect = PortraitRect();
                    list.Add(("menu", portraitRect));
                    float mw = 250f * s, mh = 46f * s;
                    list.Add(("open", new Rect(portraitRect.x, portraitRect.y - 14f * s - mh, mw, mh)));
                    list.Add(("name", new Rect(portraitRect.x, portraitRect.y - 2f * (14f * s + mh), mw, mh)));
                    break;
                case Stage.Name:
                    var field = NameField();
                    float gap = 16f * s;
                    list.Add(("field", field));
                    list.Add(("keep", new Rect(field.x - gap - 100f * s, field.y, 100f * s, field.height)));
                    list.Add(("ok", new Rect(field.xMax + gap, field.y, 130f * s, field.height)));
                    break;
                case Stage.Viewfinder:
                case Stage.Refused:
                case Stage.Failed:
                    float sw = 170f * s, sh = 96f * s;
                    list.Add((stage == Stage.Viewfinder ? "shutter" : "open", new Rect(safe.center.x - sw / 2f, safe.yMin + 40f * s, sw, sh)));
                    list.Add(("skip", new Rect(safe.xMin + 28f * s, safe.yMin + 52f * s, 100f * s, 72f * s)));
                    if (stage == Stage.Viewfinder && feed != null && devices.Length > 1)
                        list.Add(("flip", new Rect(safe.xMax - 28f * s - 100f * s, safe.yMin + 52f * s, 100f * s, 72f * s)));
                    break;
            }
            return list;
        }

        // One row under the title, X | field | OK, at the top of the screen so the keyboard never
        // covers it; origin bottom-left.
        Rect NameField()
        {
            float s = Scale;
            var safe = HudLayout.Safe;
            float row = Mathf.Min(safe.width - 72f * s, 900f * s), h = 88f * s;
            float w = row - 100f * s - 130f * s - 32f * s;
            return new Rect(safe.center.x - row / 2f + 100f * s + 16f * s, safe.yMax - 96f * s - h, w, h);
        }

        // Touches on the card and the chip never become a drag or a jump. The HUD owns
        // DragInput.IgnoreAt and may set it after this component starts, so the hook re-chains
        // whenever it changes.
        void HookDrag()
        {
            if (drag == null || drag.IgnoreAt == ignore) return;
            var previous = drag.IgnoreAt;
            ignore = p => (previous != null && previous(p)) || Hit(Buttons(), p) != null;
            drag.IgnoreAt = ignore;
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

            HookDrag();
            if (stage == Stage.Closed && !NameAsked && game != null && game.IsReady && (hud == null || !hud.Paused) && FindAnyObjectByType<LoadingCard>() == null
                && !(Race.RaceDirector.Instance is Race.RaceDirector race && (race.Gridded || race.Locked))) OpenName();
            if (stage == Stage.Menu && !PortraitVisible) stage = Stage.Closed;
            if (stage == Stage.Name) UpdateName();
            var buttons = Buttons();
            if (down && stage == Stage.Menu && Hit(buttons, p) == null) stage = Stage.Closed;
            if (down) pressed = Hit(buttons, p);
            if (!up || pressed == null) return;
            string id = pressed;
            pressed = null;
            if (Hit(buttons, p) == id) Press(id);
        }

        // What a tap on a button does; AvatarUiCapture drives the screens through it too.
        internal void Press(string id)
        {
            if (id == "menu") stage = stage == Stage.Menu ? Stage.Closed : Stage.Menu;
            else if (id == "name") OpenName();
            else if (id == "ok") CloseName(true);
            else if (id == "keep") CloseName(false);
            else if (id == "field") ShowKeyboard();
            else if (id == "open") Open();
            else if (id == "shutter") Shoot();
            else if (id == "flip") Flip();
            else if (id == "skip") Close();
        }

        internal string StageName => stage.ToString();

        internal bool Showing(string id) => Buttons().Any(b => b.id == id);

        void OnGUI()
        {
            GUI.depth = -10;
            var type = Event.current.type;
            // Full screen, nothing underneath (the HUD's pause menu chips) may take a tap.
            if (FullScreen && type != EventType.Repaint && type != EventType.Layout)
            {
                Event.current.Use();
                return;
            }
            if (type != EventType.Repaint) return;
            float s = Scale;

            if (PauseChipVisible)
            {
                var chip = PauseChipRect();
                bool down = pressed == "open";
                BeginShapes();
                Para(chip, down ? White : Ink);
                CameraPictogram(new Vector2(chip.x + chip.height * Lean * 0.5f + 34f * s, chip.center.y), chip.height * 0.2f, down ? Ink : White);
                EndShapes();
                Label(new Rect(chip.x + 52f * s, chip.y, chip.width, chip.height), "AVATAR", down ? Ink : White, 18f * s);
                return;
            }
            if (stage == Stage.Closed || stage == Stage.Menu)
            {
                if (PortraitVisible) DrawPortrait();
                if (stage == Stage.Menu) DrawMenu();
                return;
            }
            if (stage == Stage.Name)
            {
                DrawName();
                return;
            }

            DrawPhoto();
            bool refused = stage == Stage.Refused;
            string text = stage == Stage.Uploading ? "SUITING UP" : refused ? "NO ONE IN SHOT" : stage == Stage.Failed ? failure : "FULL BODY";
            var panel = TagRect(text);
            BeginShapes();
            if (refused || stage == Stage.Failed) Dim();
            if (stage == Stage.Viewfinder) Silhouette(s);
            Para(panel, refused ? Sunshine : Ink);
            if (stage == Stage.Uploading) Segments(new Rect(panel.x + 10f * s, panel.y - 26f * s, panel.width, 14f * s));
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "shutter" || id == "open") Plate(rect, Sunshine, Ink, down, r => CameraPictogram(r.center, r.height * 0.3f, down ? Sunshine : Ink));
                else if (id == "flip") Plate(rect, Ink, White, down, r => FlipPictogram(r.center, r.height * 0.26f, down ? Ink : White));
                else Plate(rect, Ink, White, down, r => ClosePictogram(r.center, r.height * 0.24f, down ? Ink : White));
            }
            EndShapes();
            Label(panel, text, refused ? Ink : Sunshine, 34f * s);
        }

        // The portrait button: the head of whatever the player wears, in a circle with a thin ring in
        // the player's colour. Pressed shifts down-right and shrinks slightly, like every plate.
        void DrawPortrait()
        {
            float s = Scale;
            var rect = PortraitRect();
            if (pressed == "menu")
            {
                var c = rect.center + new Vector2(3f * s, -3f * s);
                rect = new Rect(c.x - rect.width * 0.46f, c.y - rect.height * 0.46f, rect.width * 0.92f, rect.height * 0.92f);
            }
            GUI.DrawTexture(new Rect(rect.x, Screen.height - rect.yMax, rect.width, rect.height), portrait);
            BeginShapes();
            Ring(rect.center, rect.width * 0.5f, 4f * s, PlayerColor);
            EndShapes();
        }

        void DrawMenu()
        {
            float s = Scale;
            var chips = Buttons().Where(b => b.id != "menu").ToList();
            BeginShapes();
            foreach (var (id, rect) in chips) Para(rect, pressed == id ? White : Ink);
            EndShapes();
            foreach (var (id, rect) in chips)
                Label(rect, id == "open" ? "RETAKE PHOTO" : "CHANGE NAME", pressed == id ? Ink : White, 20f * s);
        }

        // YOUR NAME: a big white field with the name (or the grey "Racer NN" it keeps), an OK tick
        // and a skip.
        void DrawName()
        {
            float s = Scale;
            var title = TagRect("YOUR NAME");
            var field = NameField();
            BeginShapes();
            Dim();
            Para(title, Ink);
            Para(new Rect(field.x - 4f * s, field.y - 4f * s, field.width + 8f * s, field.height + 8f * s), Ink);
            Para(field, White);
            foreach (var (id, rect) in Buttons())
            {
                bool down = pressed == id;
                if (id == "ok") Plate(rect, Sunshine, Ink, down, r => TickPictogram(r.center, r.height * 0.26f, down ? Sunshine : Ink));
                else if (id == "keep") Plate(rect, Ink, White, down, r => ClosePictogram(r.center, r.height * 0.22f, down ? Ink : White));
            }
            EndShapes();
            Label(title, "YOUR NAME", Sunshine, 34f * s);
            bool caret = typed.Length < MaxName && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f;
            string shown = typed.Length > 0 ? typed + (caret ? "|" : "") : (caret ? "|" : "") + placeholder;
            Label(field, shown, typed.Length > 0 ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.35f), 40f * s);
        }

        void LateUpdate()
        {
            if (portraitDirty && worn != null) RenderPortrait();
        }

        // Renders the worn model's head once into a small cached texture with a round alpha mask,
        // far above the city so nothing shadows it, on a layer only this camera draws.
        void RenderPortrait()
        {
            portraitDirty = false;
            var root = worn.Instantiate(material);
            root.transform.SetPositionAndRotation(new Vector3(0f, 2000f, 0f), Quaternion.identity);
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PortraitLayer;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Destroy(root);
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float height = bounds.size.y;
            var head = new Vector3(bounds.center.x, bounds.max.y - height * 0.1f, bounds.center.z);

            var cam = new GameObject("Avatar Portrait Camera").AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << PortraitLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PortraitSky;
            cam.fieldOfView = 20f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.transform.position = head + new Vector3(0f, height * 0.02f, height * 0.85f);
            cam.transform.LookAt(head);

            var rt = RenderTexture.GetTemporary(PortraitSize, PortraitSize, 24);
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            if (portrait == null) portrait = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false) { name = "AvatarPortrait" };
            portrait.ReadPixels(new Rect(0, 0, PortraitSize, PortraitSize), 0, 0);
            RenderTexture.active = previous;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            DestroyImmediate(cam.gameObject);
            DestroyImmediate(root);

            var pixels = portrait.GetPixels32();
            float radius = PortraitSize * 0.5f;
            for (int y = 0; y < PortraitSize; y++)
                for (int x = 0; x < PortraitSize; x++)
                {
                    float d = new Vector2(x + 0.5f - radius, y + 0.5f - radius).magnitude;
                    pixels[y * PortraitSize + x].a = (byte)(Mathf.Clamp01(radius - d) * 255f);
                }
            portrait.SetPixels32(pixels);
            portrait.Apply();
            Debug.Log("[Avatar] portrait rendered");
        }

        static void Dim()
        {
            var c = new Color(Ink.r, Ink.g, Ink.b, 0.55f);
            Tri(new Vector2(0, 0), new Vector2(Screen.width, 0), new Vector2(Screen.width, Screen.height), c);
            Tri(new Vector2(0, 0), new Vector2(Screen.width, Screen.height), new Vector2(0, Screen.height), c);
        }

        // Aspect-fill: the camera feed rotated upright (mirrored like a mirror for the front camera),
        // or the frozen shot after the shutter, or the test photo.
        void DrawPhoto()
        {
            GUI.color = Ink;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Texture tex = stage != Stage.Viewfinder ? still : feed != null ? feed : testPhoto;
            if (tex == null || tex.width <= 16) return;

            int angle = 0;
            bool mirrorY = false, mirrorX = false;
            if (tex == feed)
            {
                angle = ((feed.videoRotationAngle % 360) + 360) % 360;
                mirrorY = feed.videoVerticallyMirrored;
                mirrorX = deviceIndex >= 0 && deviceIndex < devices.Length && devices[deviceIndex].isFrontFacing;
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

        GUIStyle Style(float size)
        {
            if (label == null) label = new GUIStyle { font = font, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            label.fontSize = Mathf.RoundToInt(size);
            return label;
        }

        float TextWidth(string text, float size) => Style(size).CalcSize(Dogpatch.Gameplay.HudLayout.Text(text)).x;

        // Text inside a panel given in bottom-left screen pixels.
        void Label(Rect rect, string text, Color color, float size)
        {
            var style = Style(size);
            style.normal.textColor = color;
            var r = new Rect(rect.x + rect.height * Lean * 0.5f + 18f * Scale, Screen.height - rect.yMax, rect.width, rect.height);
            GUI.Label(r, text, style);
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

        static void TickPictogram(Vector2 c, float k, Color stroke)
        {
            float t = Mathf.Max(2f, k * 0.34f);
            var knee = c + new Vector2(-k * 0.35f, -k * 0.75f);
            Line(c + new Vector2(-k * 1.1f, 0f), knee, t, stroke);
            Line(knee, c + new Vector2(k * 1.2f, k * 0.85f), t, stroke);
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
