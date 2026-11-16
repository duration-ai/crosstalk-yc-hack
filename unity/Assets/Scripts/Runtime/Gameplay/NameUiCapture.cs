using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Headless screenshots of the YOUR NAME prompt, inert unless the player is launched with
    // -nameUiCapture <dir> (pair it with -offline). Forgets the saved name, waits for the first-launch
    // prompt, types NAME_CAPTURE_TEXT (default Sudhir), taps OK, walks into the start ring, shoots the
    // lobby roster, then opens the portrait menu.
    public sealed class NameUiCapture : MonoBehaviour
    {
        string output;
        AvatarCapture capture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-nameUiCapture");
            if (i < 0 || i + 1 >= args.Length) return;
            PlayerPrefs.DeleteKey("dogpatch.nameAsked");
            PlayerPrefs.DeleteKey("dogpatch.racerTicket");
            PlayerPrefs.DeleteKey(Race.RaceDirector.NameKey);
            PlayerPrefs.Save();
            Application.runInBackground = true;
            new GameObject("NameUiCapture").AddComponent<NameUiCapture>().output = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            GameController game;
            float deadline = Time.realtimeSinceStartup + 180f;
            while ((game = FindAnyObjectByType<GameController>()) == null || !game.IsReady || (capture = game.GetComponent<AvatarCapture>()) == null
                || capture.StageName != "Name" || Race.RaceDirector.Instance == null)
            {
                if (Time.realtimeSinceStartup > deadline) { Finish("the name prompt never opened"); yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("01-name-prompt");

            string name = Environment.GetEnvironmentVariable("NAME_CAPTURE_TEXT") ?? "Sudhir";
            for (int n = 1; n <= name.Length; n++)
            {
                capture.Type(name.Substring(0, n));
                yield return new WaitForSecondsRealtime(0.12f);
            }
            yield return Shot("02-name-typed");
            capture.Press("ok");
            yield return null;
            var race = Race.RaceDirector.Instance;
            Debug.Log($"[NameUiCapture] stage {capture.StageName}, profile {game.Profile?.displayName}, MyName {race.MyName}");

            game.Player.Teleport(race.RingSpot(0), game.Player.transform.eulerAngles.y);
            float until = Time.realtimeSinceStartup + 30f;
            while (!HudLayout.LobbyShowing && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            var me = race.Last?.racers?.Find(r => r.playerId == race.MyId);
            Debug.Log($"[NameUiCapture] lobby {(HudLayout.LobbyShowing ? "showing" : "missing")}, my roster name {me?.displayName}");
            yield return Shot("03-lobby-roster");

            game.Player.Teleport(game.Player.transform.position + new Vector3(60f, 0f, 60f), 0f);
            until = Time.realtimeSinceStartup + 20f;
            while (!capture.Showing("menu") && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("03b-racer-ticket");
            capture.Press("menu");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("04-portrait-menu");
            Finish(me != null && me.displayName == name ? null : "roster does not show " + name);
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
            Debug.Log($"[NameUiCapture] {name} stage={capture.StageName} -> {path}");
        }

        void Finish(string error)
        {
            if (error != null) Debug.LogError("[NameUiCapture] " + error);
            else Debug.Log("[NameUiCapture] done");
            Application.Quit(error == null ? 0 : 1);
        }
    }
}
