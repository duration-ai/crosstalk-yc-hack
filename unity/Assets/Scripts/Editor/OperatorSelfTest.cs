using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dogpatch.Operator;
using Dogpatch.Operator.Voice;
using UnityEditor;
using UnityEngine;

namespace Dogpatch.EditorTools
{
    // Plain checks for the voice Operator's pure pieces (resampler, pre-roll, ranking, snapshot
    // and Convex/Scribe JSON, effect registry), run headless by scripts/operator-selftest.sh:
    //   Unity -batchmode -nographics -projectPath unity -executeMethod Dogpatch.EditorTools.OperatorSelfTest.Run
    public static class OperatorSelfTest
    {
        static int failures;
        static int passes;

        static void Check(bool ok, string what)
        {
            if (ok) passes++;
            else
            {
                failures++;
                Debug.LogError("[SelfTest] FAIL " + what);
            }
        }

        public static void Run()
        {
            failures = passes = 0;
            foreach (var test in new Action[] { Resampler, PreRoll, Ranking, SnapshotJson, ConvexJson, Scribe, Registry, Classes, CallResult })
            {
                try { test(); }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"[SelfTest] FAIL {test.Method.Name} threw {e}");
                }
            }
            Debug.Log($"[SelfTest] {passes} passed, {failures} failed");
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static float[] Sine(int rate, float hz, int frames, int channels)
        {
            var data = new float[frames * channels];
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < channels; c++)
                    data[i * channels + c] = 0.5f * Mathf.Sin(2f * Mathf.PI * hz * i / rate);
            return data;
        }

        static void Resampler()
        {
            var input = Sine(48000, 440f, 48000, 2);
            var whole = new List<short>();
            new PcmResampler(48000, 16000).Process(input, 48000, 2, whole);
            Check(Math.Abs(whole.Count - 16000) <= 1, "48 kHz stereo second -> 16000 mono samples, got " + whole.Count);
            float worst = 0f;
            for (int i = 0; i < whole.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(whole[i] / 32767f - 0.5f * Mathf.Sin(2f * Mathf.PI * 440f * i / 16000f)));
            Check(worst < 0.01f, "resampled sine tracks the 16 kHz sine (worst error " + worst + ")");

            var chunked = new List<short>();
            var r = new PcmResampler(48000, 16000);
            int at = 0;
            var rng = new System.Random(3);
            while (at < 48000)
            {
                int n = Math.Min(48000 - at, 1 + rng.Next(2000));
                var part = new float[n * 2];
                Array.Copy(input, at * 2, part, 0, n * 2);
                r.Process(part, n, 2, chunked);
                at += n;
            }
            Check(chunked.SequenceEqual(whole), "chunk boundaries do not change the output");

            var up = new List<short>();
            new PcmResampler(8000, 16000).Process(Sine(8000, 200f, 800, 1), 800, 1, up);
            Check(Math.Abs(up.Count - 1600) <= 2, "8 kHz -> 16 kHz doubles the samples, got " + up.Count);
            Check(PcmResampler.ToInt16(2f) == short.MaxValue && PcmResampler.ToInt16(-2f) == -short.MaxValue, "clipping clamps to int16");
        }

        static void PreRoll()
        {
            var ring = new PcmRing(8000);
            var samples = new List<short>();
            for (short i = 0; i < 10000; i++) samples.Add(i);
            ring.Write(samples);
            var drained = new List<short>();
            ring.DrainTo(drained);
            Check(drained.Count == 8000 && drained[0] == 2000 && drained[7999] == 9999, "pre-roll keeps the newest 0.5 s in order");
            Check(ring.Count == 0, "draining empties the pre-roll");
        }

        static void Ranking()
        {
            var centreLarge = new Rect(0.3f, 0.3f, 0.4f, 0.4f);
            var leftSmall = new Rect(0.02f, 0.4f, 0.05f, 0.05f);
            Check(ViewRanker.Screen(centreLarge) == "centre, large", "screen label centre, large: " + ViewRanker.Screen(centreLarge));
            Check(ViewRanker.Screen(leftSmall) == "left, small", "screen label left, small: " + ViewRanker.Screen(leftSmall));
            Check(ViewRanker.Screen(new Rect(0.75f, 0.4f, 0.15f, 0.2f)) == "right, medium", "screen label right, medium");
            Check(ViewRanker.Score(centreLarge, 20f) > ViewRanker.Score(leftSmall, 20f), "centred and large outranks off to the side and small");
            Check(ViewRanker.Score(centreLarge, 10f) > ViewRanker.Score(centreLarge, 100f), "closer outranks further");

            var candidates = new List<(Rect, float, bool)>();
            for (int i = 0; i < 12; i++) candidates.Add((new Rect(0.05f * i, 0.4f, 0.1f, 0.1f), 10f + i, false));
            candidates.Add((new Rect(0.4f, 0f, 0.2f, 0.2f), 2f, true));
            var picked = ViewRanker.Rank(candidates, 8, i => i != 9);
            Check(picked.Count == 8, "top 8 only, got " + picked.Count);
            Check(picked.Contains(12), "a forced entity (veh_me) is always in");
            Check(!picked.Contains(9), "occluded entities are skipped");
            var scores = picked.Where(i => i != 12).Select(i => ViewRanker.Score(candidates[i].Item1, candidates[i].Item2)).ToList();
            Check(scores.SequenceEqual(scores.OrderByDescending(x => x)), "picked best first");
        }

        static void SnapshotJson()
        {
            var view = new OperatorView
            {
                mode = "driving",
                timeOfDay = "day",
                weather = "clear",
                entities = new[]
                {
                    new ViewEntity { id = "veh_me", @class = "car", desc = "your compact hatchback", color = "blue", screen = "centre, large", distance_m = 3f, view_rank = 1, @protected = false, controlledBy = "me" },
                    new ViewEntity { id = "bld_12", @class = "building", desc = "YC building", color = "white", screen = "right, medium", distance_m = 48f, view_rank = 2, @protected = true, controlledBy = "" },
                },
            };
            var json = JsonUtility.ToJson(view);
            Check(json.Contains("\"class\":\"car\"") && json.Contains("\"protected\":true") && json.Contains("\"distance_m\":48") && json.Contains("\"view_rank\":2") && json.Contains("\"controlledBy\":\"me\""), "snapshot JSON uses the contract's field names: " + json);
            var args = JsonUtility.ToJson(new OperatorCallArgs { roomId = "r1", playerId = "p1", transcript = "make it rain", view = view, clientTimings = new ClientTimings { sttMs = 420 } });
            Check(args.StartsWith("{\"roomId\":\"r1\",\"playerId\":\"p1\",\"transcript\":\"make it rain\",\"view\":{") && args.Contains("\"clientTimings\":{\"sttMs\":420"), "operator:call args shape: " + args);
        }

        static void ConvexJson()
        {
            var body = "{\"status\":\"success\",\"value\":{\"effects\":[{\"id\":\"e1\",\"label\":\"Rain \\\"heavy\\\"\"}]},\"logLines\":[]}";
            var value = ConvexHttpClient.Unwrap(body, 200, null);
            Check(value == "{\"effects\":[{\"id\":\"e1\",\"label\":\"Rain \\\"heavy\\\"\"}]}", "Unwrap returns the raw value: " + value);
            Check(JsonUtility.FromJson<ActiveEffectList>(value).effects[0].label == "Rain \"heavy\"", "value parses with JsonUtility");
            try
            {
                ConvexHttpClient.Unwrap("{\"status\":\"error\",\"errorMessage\":\"Uncaught ConvexError\",\"errorData\":{\"code\":\"RATE_LIMITED\",\"message\":\"Too many tokens\"}}", 400, null);
                Check(false, "error envelope throws");
            }
            catch (ConvexException e)
            {
                Check(e.Code == "RATE_LIMITED" && e.Message == "Too many tokens", "ConvexError data becomes Code/Message: " + e.Code + " " + e.Message);
            }
            try
            {
                ConvexHttpClient.Unwrap(null, 0, "Cannot resolve destination host");
                Check(false, "transport failure throws");
            }
            catch (ConvexException e)
            {
                Check(e.Code == "NETWORK", "transport failure is NETWORK");
            }
            Check(ConvexHttpClient.Envelope("operator:call", "{\"a\":1}") == "{\"path\":\"operator:call\",\"args\":{\"a\":1},\"format\":\"json\"}", "HTTP API envelope");
            Check(JsonText.Unquote(JsonText.Quote("say \"hi\"\n\\ ok")) == "say \"hi\"\n\\ ok", "Quote/Unquote round trip");
            Check(JsonText.Field("{\"a\":{\"b\":[1,{\"c\":\"}\"}]},\"d\":null}", "d") == "null", "Field skips nested values and strings with braces");
        }

        static void Scribe()
        {
            var config = new OperatorConfig();
            var url = ScribeSession.Url(config, "sutkn_1/+");
            Check(url.StartsWith("wss://api.elevenlabs.io/v1/speech-to-text/realtime?model_id=scribe_v2_realtime&token=sutkn_1%2F%2B&audio_format=pcm_16000&commit_strategy=manual&language_code=en"), "scribe URL: " + url);
            Check(url.EndsWith("&keyterms=Waymo&keyterms=Zoox&keyterms=Lyft&keyterms=YC&keyterms=nitro"), "keyterms repeat as separate query params");
            config.commitStrategy = "vad";
            Check(ScribeSession.Url(config, "t").Contains("commit_strategy=vad"), "commit strategy is configurable");
            var chunk = ScribeSession.Chunk(new short[] { 1, -2, 300 }, 0, 3, true);
            var b64 = JsonText.Unquote(JsonText.Field(chunk, "audio_base_64"));
            var bytes = Convert.FromBase64String(b64);
            Check(bytes.Length == 6 && BitConverter.ToInt16(bytes, 2) == -2 && BitConverter.ToInt16(bytes, 4) == 300, "audio is little-endian int16 in base64");
            Check(JsonText.Unquote(JsonText.Field(chunk, "message_type")) == "input_audio_chunk" && JsonText.Field(chunk, "commit") == "true" && JsonText.Field(chunk, "sample_rate") == "16000", "input_audio_chunk shape: " + chunk);
        }

        static void Registry()
        {
            var registry = EffectRegistry.Default();
            Check(EffectRegistry.Catalogue.Length == 73 && EffectRegistry.Catalogue.Distinct().Count() == 73, "73 catalogue ids, no duplicates");
            Check(!registry.Has("undo_last"), "no undo effect");
            foreach (var id in EffectRegistry.Catalogue) Check(registry.Has(id), "registered: " + id);
            foreach (var id in EffectRegistry.Built) Check(!registry.IsStub(id), "built for real: " + id);
            foreach (var id in registry.Ids)
                if (!registry.IsStub(id) && !EffectRegistry.Counters.Contains(id)) Check(EffectRegistry.Built.Contains(id), "listed in built-effects.json: " + id);
            var unbuilt = EffectRegistry.Catalogue.FirstOrDefault(id => registry.IsStub(id));
            Check(unbuilt == null || registry.Create(unbuilt, null, new ActiveEffect()) is StubEffect, "unbuilt ids are logged stubs");
            Check(registry.Create("not_in_catalogue", null, new ActiveEffect()) is StubEffect, "unknown ids fall back to a stub");
        }

        static void Classes()
        {
            Check(EntityClasses.IsA("waymo", "vehicle") && EntityClasses.IsA("zoox", "car") && EntityClasses.IsA("bus", "vehicle"), "vehicle > car > waymo/zoox, vehicle > bus");
            Check(EntityClasses.IsA("delivery_rider", "person") && EntityClasses.IsA("tree", "plant") && !EntityClasses.IsA("car", "person") && !EntityClasses.IsA("vehicle", "car"), "person/plant hierarchy; parents are not their children");
            Check(ColorWords.Nearest(Color.white) == "white" && ColorWords.Nearest(new Color(0.9f, 0.1f, 0.1f)) == "red" && ColorWords.Nearest(new Color(0.18f, 0.55f, 1f)) == "blue", "colour words");
        }

        static void CallResult()
        {
            var json = "{\"outcome\":\"refused\",\"effect\":null,\"refusal\":{\"reason\":\"protected\",\"message\":\"That landmark's protected.\"},\"line\":\"That landmark's protected.\",\"serverNow\":1763236800123,\"timings\":{\"jevMs\":412,\"totalMs\":530,\"attempts\":1}}";
            var result = JsonUtility.FromJson<OperatorCallResult>(json);
            Check(result.outcome == "refused" && (result.effect == null || !result.effect.IsSet) && result.refusal.reason == "protected", "refused call result");
            Check(Math.Abs(result.serverNow - 1763236800123.0) < 1, "epoch ms survive as double");
            var applied = JsonUtility.FromJson<OperatorCallResult>("{\"outcome\":\"applied\",\"effect\":{\"id\":\"k1\",\"effect\":\"rain\",\"scope\":\"world\",\"target\":\"\",\"targetClass\":\"\",\"strength\":\"medium\",\"color\":\"\",\"theme\":\"\",\"instant\":false,\"status\":\"active\",\"startsAt\":1763236800000,\"endsAt\":1763236815000,\"durationS\":15,\"label\":\"Rain\",\"requestedBy\":\"p1\",\"transcript\":\"make it rain\"},\"refusal\":null,\"line\":\"Rain!\",\"serverNow\":1763236800000}");
            Check(applied.effect.IsSet && applied.effect.endsAt - applied.effect.startsAt == 15000 && applied.line == "Rain!", "applied call result carries the effect row");
            Check(ConvexOperator.Refused("No teleporting, sorry.") == "No teleporting, sorry." && ConvexOperator.Refused("this message is far too long for the hud") == "Try again, sugar.", "toast lines stay within four words");
        }
    }
}
