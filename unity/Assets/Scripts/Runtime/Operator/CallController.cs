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

        public CallState State { get; private set; }
        public float StateSince { get; private set; }
        public string Line { get; private set; }
        public int Calls { get; private set; }

        public event Action<OperatorResponse> Answered;

        void Set(CallState state)
        {
            State = state;
            StateSince = Time.time;
        }

        public void HangUp() => Set(CallState.None);

        public bool Grab(Payphone phone)
        {
            if (State != CallState.None || !phone.Ringing) return false;
            phone.ReadyAt = Time.time + PhoneRest;
            Set(CallState.Holding);
            Debug.Log("[SpeedDial] handset grabbed, call is live");
            return true;
        }

        public void BeginTalk()
        {
            if (State == CallState.Holding) Set(CallState.Talking);
        }

        public void EndTalk()
        {
            if (State != CallState.Talking) return;
            if (Time.time - StateSince < HoldMinimum)
            {
                Set(CallState.Holding);
                return;
            }
            _ = SendAsync();
        }

        async Task SendAsync()
        {
            Set(CallState.Waiting);
            try
            {
                var reply = await Operator.CallAsync(new OperatorCall { PlayerId = PlayerId(), RoomId = RoomId() });
                Line = reply.line;
                Calls++;
                Set(CallState.Answered);
                Debug.Log($"[SpeedDial] Operator: \"{reply.line}\" -> {reply.command.type} ({reply.provider})");
                Answered?.Invoke(reply);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SpeedDial] Operator call failed: " + e.Message);
                Set(CallState.Holding);
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
