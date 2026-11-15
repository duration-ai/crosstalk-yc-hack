using System;
using System.Threading.Tasks;
using Dogpatch.Audio;
using Dogpatch.Feel;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Operator
{
    public enum CallState
    {
        None,
        Talking,
        Waiting,
        Answered,
    }

    // The Operator on the cell-phone button: always in reach from the HUD, on foot or driving.
    // Holding the button is talking: pressing snapshots the View, opens the transcriber and streams
    // the microphone; letting go sends the final transcript, and the Operator answers with a world
    // change and a line. Each call needs an ask (Asks, when set); with none the button turns her
    // down in one line. Buzz is the phone ringing in your pocket (a new ask, the race's GO). Say
    // sends typed words the same way, for live tests only.
    public sealed class CallController : MonoBehaviour
    {
        public const string NoAsksLine = "No asks! Find a pink token.";
        const float HoldMinimum = 0.35f;
        const float AnswerShown = 2.6f;

        public GameController Game;
        public IOperator Operator;
        public Func<string> PlayerId;
        public Func<string> RoomId;
        public Func<int> Asks;
        public IVoiceInput Voice;
        public Func<OperatorView> View;

        public CallState State { get; private set; }
        public float StateSince { get; private set; }
        public string Line { get; private set; }
        public int Calls { get; private set; }
        public string Heard { get; private set; } = "";
        public string Partial => State == CallState.Talking || State == CallState.Waiting ? Voice?.Partial ?? "" : "";
        public float BuzzedAt { get; private set; } = -10f;
        public float TurnedDownAt { get; private set; } = -10f;
        public bool HasAsks => Asks == null || Asks() > 0;

        OperatorView snapshot;
        int lastAsks = -1;

        public event Action<OperatorResponse> Answered;

        void Set(CallState state)
        {
            State = state;
            StateSince = Time.time;
            if (state == CallState.None) Voice?.Close();
        }

        public void HangUp() => Set(CallState.None);

        public void Buzz()
        {
            if (!HasAsks) return;
            BuzzedAt = Time.time;
            SoundSystem.Play2D("call_ring", Bus.Ui, 0.6f, 1f, 250);
            Haptics.Notify(HapticNotice.Warning);
        }

        public void BeginTalk()
        {
            if (State != CallState.None && State != CallState.Answered) return;
            if (!HasAsks)
            {
                Line = NoAsksLine;
                TurnedDownAt = Time.time;
                Haptics.Notify(HapticNotice.Error);
                Debug.Log("[SpeedDial] no asks left: the Operator turned the call down");
                return;
            }
            snapshot = View?.Invoke();
            Voice?.Open();
            Voice?.BeginTalk();
            Set(CallState.Talking);
        }

        public void EndTalk()
        {
            if (State != CallState.Talking) return;
            if (Time.time - StateSince < HoldMinimum)
            {
                Voice?.Cancel();
                Set(CallState.None);
                return;
            }
            _ = SendAsync(null);
        }

        // Typed words straight to the Operator, for live tests only (the dev-flag text field,
        // speeddial://operator?say=..., -operator-say): no microphone needed.
        public bool Say(string text)
        {
            if (State == CallState.Talking || State == CallState.Waiting || string.IsNullOrWhiteSpace(text)) return false;
            snapshot = View?.Invoke();
            _ = SendAsync(text.Trim());
            return true;
        }

        async Task SendAsync(string typed)
        {
            Set(CallState.Waiting);
            try
            {
                string text = typed;
                int sttMs = 0;
                if (text == null && Voice != null)
                {
                    var heard = await Voice.FinishAsync();
                    text = heard.Text;
                    sttMs = heard.SttMs;
                }
                Heard = text ?? "";
                var reply = await Operator.CallAsync(new OperatorCall { PlayerId = PlayerId(), RoomId = RoomId(), Text = text, View = snapshot, SttMs = sttMs });
                Line = reply.line;
                Calls++;
                Set(CallState.Answered);
                Debug.Log($"[SpeedDial] Operator: \"{reply.line}\" -> {reply.command?.type ?? reply.effect?.effect ?? reply.outcome} ({reply.provider})");
                Answered?.Invoke(reply);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SpeedDial] Operator call failed: " + e.Message);
                Set(CallState.None);
            }
        }

        void Update()
        {
            if (Game == null || !Game.IsReady) return;
            if (State == CallState.Answered && Time.time - StateSince > AnswerShown) Set(CallState.None);
            int asks = Asks?.Invoke() ?? -1;
            if (lastAsks >= 0 && asks > lastAsks) Buzz();
            lastAsks = asks;
        }
    }
}
