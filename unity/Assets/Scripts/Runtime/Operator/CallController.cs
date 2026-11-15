using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Operator
{
    public enum CallState
    {
        None,
        Holding,
        Talking,
        Waiting,
        Answered,
    }

    public sealed class Payphone : MonoBehaviour
    {
        public Spot Spot;
        public float ReadyAt;

        public Vector3 HandsetPoint => transform.position + transform.forward * 0.9f;
        public bool Ringing => Time.time >= ReadyAt;
    }

    // A call from a hotline-pink payphone. Walking up to one on foot grabs the handset and the Call
    // plate slides in; the call stays with you (into a car too) until you use it. Holding Call is
    // talking; letting go sends the call to the Operator, whose answer is a world change and a line.
    // With a Voice, the plate opens the transcriber, pressing snapshots the View and streams the
    // microphone, and releasing sends the final transcript. Say sends typed words the same way.
    public sealed class CallController : MonoBehaviour
    {
        const float Reach = 1.6f;
        const float HoldMinimum = 0.35f;
        const float CallLasts = 45f;
        const float PhoneRest = 20f;

        public GameController Game;
        public IOperator Operator;
        public Func<string> PlayerId;
        public Func<string> RoomId;
        public List<Payphone> Phones = new List<Payphone>();
        public IVoiceInput Voice;
        public Func<OperatorView> View;

        public CallState State { get; private set; }
        public float StateSince { get; private set; }
        public string Line { get; private set; }
        public int Calls { get; private set; }
        public string Heard { get; private set; } = "";
        public string Partial => State == CallState.Talking || State == CallState.Waiting ? Voice?.Partial ?? "" : "";

        OperatorView snapshot;

        public event Action<OperatorResponse> Answered;

        void Set(CallState state)
        {
            State = state;
            StateSince = Time.time;
            if (state == CallState.None) Voice?.Close();
        }

        public void HangUp() => Set(CallState.None);

        public bool Grab(Payphone phone)
        {
            if (State != CallState.None || !phone.Ringing) return false;
            phone.ReadyAt = Time.time + PhoneRest;
            Set(CallState.Holding);
            Voice?.Open();
            Debug.Log("[SpeedDial] handset grabbed, call is live");
            return true;
        }

        public void BeginTalk()
        {
            if (State != CallState.Holding) return;
            snapshot = View?.Invoke();
            Voice?.BeginTalk();
            Set(CallState.Talking);
        }

        public void EndTalk()
        {
            if (State != CallState.Talking) return;
            if (Time.time - StateSince < HoldMinimum)
            {
                Voice?.Cancel();
                Set(CallState.Holding);
                return;
            }
            _ = SendAsync(null);
        }

        // Typed words straight to the Operator, for live tests only (the dev-flag text field,
        // speeddial://operator?say=..., -operator-say): no payphone or microphone needed.
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
                Set(typed == null ? CallState.Holding : CallState.None);
                if (typed == null) Voice?.Open();
            }
        }

        void Update()
        {
            if (Game == null || !Game.IsReady) return;
            if (State == CallState.Holding && Time.time - StateSince > CallLasts) Set(CallState.None);
            if (State == CallState.Answered && Time.time - StateSince > 2.6f) Set(CallState.None);
            if (State != CallState.None || Game.CurrentCar != null) return;
            var me = Game.Player.transform.position;
            foreach (var phone in Phones)
            {
                var handset = phone.HandsetPoint;
                if (Mathf.Abs(me.y - handset.y) > 2f || Vector2.Distance(new Vector2(me.x, me.z), new Vector2(handset.x, handset.z)) > Reach) continue;
                if (Grab(phone)) break;
            }
        }
    }
}
