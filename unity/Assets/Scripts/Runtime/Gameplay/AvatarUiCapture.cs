using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Headless screenshots of the selfie-avatar screens over the game, inert unless the player is
    // launched with -avatarUiCapture <dir>. Forgets any saved avatar, then walks the flow through
    // AvatarCapture.Press exactly as taps would: the free-roam portrait button, the pause menu chip, the capture
    // screen, then three shutters whose answers come from whatever -dogpatch-avatar-url serves
    // (the expected order is 422, 503, then a slow 200), and finally the avatar in the world.
    // Run with -dogpatch-avatar-test-photo so the shutter has a photo without a camera.
    public sealed class AvatarUiCapture : MonoBehaviour
    {
        string output;
        GameController game;
        AvatarCapture capture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-avatarUiCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            PlayerPrefs.DeleteKey("dogpatch.avatar");
            PlayerPrefs.Save();
            Application.runInBackground = true;
            new GameObject("AvatarUiCapture").AddComponent<AvatarUiCapture>().output = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            float deadline = Time.realtimeSinceStartup + 180f;
            while ((game = FindAnyObjectByType<GameController>()) == null || !game.IsReady || (capture = game.GetComponent<AvatarCapture>()) == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Finish("game never got ready"); yield break; }
                yield return null;
            }
            float until = Time.realtimeSinceStartup + 30f;
            while (!capture.Showing("menu") && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("01-portrait-button");

            var setPaused = typeof(Hud).GetMethod("SetPaused", BindingFlags.Instance | BindingFlags.NonPublic);
            var hud = game.GetComponent<Hud>();
            setPaused.Invoke(hud, new object[] { true });
            yield return new WaitForSecondsRealtime(0.5f);
            if (!capture.Showing("open")) { Finish("no AVATAR chip in the pause menu"); yield break; }
            yield return Shot("02-pause-menu-avatar-chip");

            capture.Press("open");
            yield return Until("Viewfinder", 5f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("03-capture-screen");

            capture.Press("shutter");
            yield return Until("Refused", 30f);
            yield return Shot("05-no-one-in-shot");

            capture.Press("open");
            yield return Until("Viewfinder", 5f);
            capture.Press("shutter");
            yield return Until("Failed", 30f);
            yield return Shot("06-try-again");

            capture.Press("open");
            yield return Until("Viewfinder", 5f);
            capture.Press("shutter");
            yield return Until("Uploading", 5f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("04-suiting-up");
            yield return Until("Closed", 90f);

            setPaused.Invoke(hud, new object[] { false });
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("07-avatar-in-world");
            if (!capture.Showing("open")) { Finish("no portrait button after the avatar changed"); yield break; }

            var follow = game.View.GetComponent<FollowCamera>();
            if (follow != null) follow.enabled = false;
            var body = game.Player.transform;
            game.View.transform.position = body.position + body.forward * 4.2f + Vector3.up * 1.6f;
            game.View.transform.LookAt(body.position + Vector3.up * 0.9f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("08-avatar-closeup");
            Finish(null);
        }

        IEnumerator Until(string stage, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (capture.StageName != stage && Time.realtimeSinceStartup < end) yield return null;
            Debug.Log($"[AvatarUiCapture] stage {capture.StageName} (wanted {stage})");
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            string path = Path.Combine(output, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log($"[AvatarUiCapture] {name} stage={capture.StageName} -> {path}");
        }

        void Finish(string error)
        {
            if (error != null) Debug.LogError("[AvatarUiCapture] " + error);
            else Debug.Log("[AvatarUiCapture] done");
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
