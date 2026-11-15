using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dogpatch.Operator.Voice
{
    // IVoiceInput over the microphone and ElevenLabs Scribe realtime. Opening the call mints a
    // single-use token (operator:mintScribeToken; keys stay on the server), opens the socket and
    // starts the microphone, keeping the last preRollMs of audio. Pressing Call sends that
    // pre-roll and then streams chunkMs chunks; releasing pads silencePadMs of silence, commits
    // (manual strategy) and takes committed_transcript, or the last partial after finalWaitMs.
    public sealed class ScribeVoice : MonoBehaviour, IVoiceInput
    {
        public OperatorConfig Config;
        public Func<Task<string>> MintToken;

        readonly MicCapture mic = new MicCapture();
        readonly List<short> resampled = new List<short>();
        readonly List<short> pending = new List<short>();
        PcmResampler resampler;
        PcmRing preRoll;
        ScribeSession session;
        Task opening;
        bool open;
        bool talking;
        bool askedPermission;
        float lastSend;
        short[] sendBuffer = new short[0];

        public string Partial { get; private set; } = "";

        public bool MicBlocked => askedPermission && !Application.HasUserAuthorization(UserAuthorization.Microphone);

        public void Open()
        {
            if (open) return;
            open = true;
            Partial = "";
            preRoll = new PcmRing(ScribeSession.SampleRate * Config.preRollMs / 1000);
            pending.Clear();
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
            if (mic.Running) return;
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                AskPermission();
                return;
            }
            if (mic.Start(48000)) resampler = new PcmResampler(mic.Rate, ScribeSession.SampleRate);
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
                var token = await MintToken();
                if (s != session) return;
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    await s.OpenAsync(Config, token, cancel.Token);
                Debug.Log($"[Operator] scribe socket open in {clock.ElapsedMilliseconds} ms ({Config.commitStrategy})");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Operator] scribe connect failed: " + e.Message);
            }
        }

        public void BeginTalk()
        {
            if (!open) Open();
            talking = true;
            Partial = "";
            pending.Clear();
            preRoll.DrainTo(pending);
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

        public void Close()
        {
            open = false;
            talking = false;
            mic.Stop();
            resampler = null;
            session?.Dispose();
            session = null;
            pending.Clear();
        }

        void Flush(bool commit)
        {
            if (pending.Count == 0 && !commit) return;
            if (sendBuffer.Length < pending.Count) sendBuffer = new short[pending.Count];
            pending.CopyTo(sendBuffer);
            session.Send(sendBuffer, pending.Count, commit);
            pending.Clear();
            lastSend = Time.unscaledTime;
        }

        void Pump()
        {
            if (!mic.Running) return;
            mic.Poll((samples, frames, channels) =>
            {
                resampled.Clear();
                resampler.Process(samples, frames, channels, resampled);
                if (talking) pending.AddRange(resampled);
                else preRoll.Write(resampled);
            });
        }

        void Update()
        {
            if (!open) return;
            if (!mic.Running) StartMic();
            Pump();
            if (talking && session != null && session.IsOpen && Time.unscaledTime - lastSend >= Config.chunkMs / 1000f) Flush(false);
            if (pending.Count > ScribeSession.SampleRate * 20) pending.RemoveRange(0, pending.Count - ScribeSession.SampleRate * 20);
        }

        void OnDestroy() => Close();
    }
}
