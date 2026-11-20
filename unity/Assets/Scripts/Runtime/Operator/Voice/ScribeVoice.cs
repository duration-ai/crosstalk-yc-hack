using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dogpatch.Operator.Voice
{
    // IVoiceInput over the microphone and ElevenLabs Scribe realtime. The microphone starts as soon as
    // it is allowed and stays warm, always keeping the last preRollMs of audio, so a call loses no
    // words to the microphone's start-up. A single-use token (operator:mintScribeToken; keys stay on
    // the server) is minted ahead of each call. Opening a call opens the socket with that token while
    // everything heard from the press on, pre-roll first, queues locally; once the socket is open the
    // queue goes up in chunkMs chunks, then the live audio. Releasing pads silencePadMs of silence,
    // commits (manual strategy) and takes committed_transcript, or the last partial after finalWaitMs.
    public sealed class ScribeVoice : MonoBehaviour, IVoiceInput
    {
        // Single-use tokens expire after 15 minutes; a spare older than this is minted again.
        const float SpareTokenSeconds = 600f;

        public OperatorConfig Config;
        public Func<Task<string>> MintToken;
        // Off for a headless test that feeds Hear itself.
        public bool UseMicrophone = true;

        readonly MicCapture mic = new MicCapture();
        readonly List<short> resampled = new List<short>();
        readonly List<short> pending = new List<short>();
        readonly Stopwatch sincePress = new Stopwatch();
        PcmResampler resampler;
        PcmRing preRoll;
        ScribeSession session;
        Task opening;
        Task<string> spareToken;
        float spareMintedAt;
        bool open;
        bool talking;
        bool askedPermission;
        bool warmTried;
        bool firstSampleLogged;
        int heardSincePress;
        float lastSend;
        short[] sendBuffer = new short[0];

        public string Partial { get; private set; } = "";

        public bool MicBlocked => askedPermission && !Application.HasUserAuthorization(UserAuthorization.Microphone);

        PcmRing Ring => preRoll ??= new PcmRing(ScribeSession.SampleRate * Config.preRollMs / 1000);

        void Start() => MintSpare();

        public void Open()
        {
            if (open) return;
            open = true;
            Partial = "";
            pending.Clear();
            sincePress.Restart();
            firstSampleLogged = false;
            heardSincePress = 0;
            StartMic();
            Connect();
        }

        // Asked at launch so the iOS prompt never lands in the middle of a race.
        public void AskPermission()
        {
            if (askedPermission || Application.HasUserAuthorization(UserAuthorization.Microphone)) return;
            askedPermission = true;
            Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        void StartMic()
        {
            if (!UseMicrophone) return;
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                AskPermission();
                return;
            }
            if (mic.Start(48000)) resampler = new PcmResampler(mic.Rate, ScribeSession.SampleRate);
        }

        void MintSpare()
        {
            if (MintToken == null) return;
            spareMintedAt = Time.realtimeSinceStartup;
            spareToken = MintToken();
        }

        // The spare token when it's fresh (or still being minted), else a new one; either way the
        // next call's spare starts minting now, one mint per call.
        Task<string> TakeToken()
        {
            var token = spareToken != null && !spareToken.IsFaulted && Time.realtimeSinceStartup - spareMintedAt < SpareTokenSeconds ? spareToken : MintToken();
            MintSpare();
            return token;
        }

        void Connect()
        {
            if (opening != null && !opening.IsCompleted) return;
            session?.Dispose();
            var s = new ScribeSession();
            s.Partial += text => Partial = text;
            session = s;
            opening = ConnectAsync(s);
        }

        async Task ConnectAsync(ScribeSession s)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var tokenTask = TakeToken();
                bool ready = tokenTask.IsCompleted;
                var token = await tokenTask;
                long tokenMs = clock.ElapsedMilliseconds;
                if (s != session) return;
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    await s.OpenAsync(Config, token, cancel.Token);
                Debug.Log($"[Operator] scribe socket open in {clock.ElapsedMilliseconds} ms ({Config.commitStrategy}; token {(ready ? "ready" : "waited " + tokenMs + " ms")}), {sincePress.ElapsedMilliseconds} ms after the press, with {heardSincePress * 1000 / ScribeSession.SampleRate} ms of audio heard by then (all of it goes up)");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Operator] scribe connect failed: " + e.Message);
            }
        }

        public void BeginTalk(bool preRoll = true)
        {
            if (!open) Open();
            talking = true;
            Partial = "";
            pending.Clear();
            Ring.DrainTo(pending);
            if (!preRoll) pending.Clear();
            if (pending.Count > 0) Debug.Log($"[Operator] talk starts with {pending.Count * 1000 / ScribeSession.SampleRate} ms of pre-roll");
            lastSend = Time.unscaledTime;
            if (session == null || (!session.IsOpen && opening.IsCompleted)) Connect();
        }

        public void Cancel()
        {
            talking = false;
            pending.Clear();
        }

        public async Task<Heard> FinishAsync()
        {
            var clock = Stopwatch.StartNew();
            talking = false;
            Pump();
            pending.AddRange(new short[ScribeSession.SampleRate * Mathf.Max(0, Config.silencePadMs) / 1000]);
            if (opening != null) await Task.WhenAny(opening, Task.Delay(3000));
            if (session == null || !session.IsOpen)
            {
                Debug.LogWarning("[Operator] no scribe session at release" + (session?.Error != null ? ": " + session.Error : ""));
                return new Heard { Text = session?.LastPartial?.Trim() ?? "", SttMs = (int)clock.ElapsedMilliseconds };
            }
            bool manual = Config.commitStrategy != "vad";
            if (pending.Count == 0 && manual) pending.AddRange(new short[ScribeSession.SampleRate / 50]);
            Flush(manual);
            var text = await session.FinalAsync(Config.finalWaitMs);
            Debug.Log($"[Operator] heard \"{text}\" {clock.ElapsedMilliseconds} ms after release");
            return new Heard { Text = text, SttMs = (int)clock.ElapsedMilliseconds };
        }

        // The microphone stays warm after the call, feeding the pre-roll for the next one.
        public void Close()
        {
            open = false;
            talking = false;
            session?.Dispose();
            session = null;
            pending.Clear();
        }

        // Sends the queue in chunks of at most chunkMs; commit rides on the last one.
        void Flush(bool commit)
        {
            if (pending.Count == 0 && !commit) return;
            if (sendBuffer.Length < pending.Count) sendBuffer = new short[pending.Count];
            pending.CopyTo(sendBuffer);
            int chunk = Mathf.Max(1, ScribeSession.SampleRate * Config.chunkMs / 1000);
            int at = 0;
            do
            {
                int count = Mathf.Min(chunk, pending.Count - at);
                session.Send(sendBuffer, at, count, commit && at + count >= pending.Count);
                at += count;
            }
            while (at < pending.Count);
            pending.Clear();
            lastSend = Time.unscaledTime;
        }

        // 16 kHz mono audio as the microphone hears it: queued for the socket while talking, else
        // into the pre-roll. Public so a headless test can stand in for the microphone.
        public void Hear(List<short> pcm)
        {
            if (!talking)
            {
                Ring.Write(pcm);
                return;
            }
            if (!firstSampleLogged && pcm.Count > 0)
            {
                firstSampleLogged = true;
                Debug.Log($"[Operator] first microphone audio {sincePress.ElapsedMilliseconds} ms after the press");
            }
            heardSincePress += pcm.Count;
            pending.AddRange(pcm);
        }

        void Pump()
        {
            if (!mic.Running) return;
            mic.Poll((samples, frames, channels) =>
            {
                resampled.Clear();
                resampler.Process(samples, frames, channels, resampled);
                Hear(resampled);
            });
        }

        void Update()
        {
            if (UseMicrophone && !mic.Running && (open || !warmTried) && Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                warmTried = true;
                StartMic();
            }
            Pump();
            if (!open) return;
            if (talking && session != null && session.IsOpen && Time.unscaledTime - lastSend >= Config.chunkMs / 1000f) Flush(false);
            if (pending.Count > ScribeSession.SampleRate * 20) pending.RemoveRange(0, pending.Count - ScribeSession.SampleRate * 20);
        }

        void OnDestroy()
        {
            Close();
            mic.Release();
        }
    }
}
