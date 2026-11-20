using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dogpatch.Operator.Voice
{
    // Headless check that no words are lost between pressing Call and the Scribe socket opening:
    // -scribe-gap-test <pcm> <mintMs> <token> [<token>...] plays 16 kHz mono int16 speech (with the
    // speech starting ~100 ms in) into ScribeVoice as if the microphone heard it from the press on,
    // then releases and logs what Scribe heard. mintMs 0: the spare token is ready at the press.
    // Above 0: no spare yet (a call right after launch), and the token takes mintMs to arrive.
    public sealed class ScribeGapTest : MonoBehaviour
    {
        string pcmPath;
        int mintMs;
        readonly Queue<string> tokens = new Queue<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-scribe-gap-test");
            if (at < 0 || at + 3 >= args.Length) return;
            Application.runInBackground = true;
            var test = new GameObject("ScribeGapTest").AddComponent<ScribeGapTest>();
            DontDestroyOnLoad(test.gameObject);
            test.pcmPath = args[at + 1];
            test.mintMs = int.Parse(args[at + 2]);
            for (int i = at + 3; i < args.Length && !args[i].StartsWith("-"); i++) test.tokens.Enqueue(args[i]);
        }

        async Task<string> Mint()
        {
            await Task.Delay(mintMs);
            return tokens.Count > 0 ? tokens.Dequeue() : "no-token-left";
        }

        IEnumerator Start()
        {
            var bytes = File.ReadAllBytes(pcmPath);
            var speech = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, speech, 0, speech.Length * 2);

            var voice = gameObject.AddComponent<ScribeVoice>();
            voice.Config = OperatorConfig.Load();
            voice.UseMicrophone = false;
            if (mintMs == 0) voice.MintToken = Mint;
            yield return new WaitForSecondsRealtime(15f);
            voice.MintToken = Mint;
            Debug.Log($"[ScribeGapTest] pressing at {Time.realtimeSinceStartup:F1} s, frame {Time.unscaledDeltaTime * 1000f:F0} ms");

            voice.Open();
            voice.BeginTalk(false);
            var clock = Stopwatch.StartNew();
            int fed = 0;
            var block = new List<short>();
            while (fed < speech.Length)
            {
                int due = Mathf.Min(speech.Length, (int)(clock.ElapsedMilliseconds * ScribeSession.SampleRate / 1000));
                block.Clear();
                for (; fed < due; fed++) block.Add(speech[fed]);
                voice.Hear(block);
                yield return null;
            }
            var heard = voice.FinishAsync();
            while (!heard.IsCompleted) yield return null;
            Debug.Log($"[ScribeGapTest] mint {mintMs} ms, heard \"{heard.Result.Text}\"");
            Application.Quit();
        }
    }
}
