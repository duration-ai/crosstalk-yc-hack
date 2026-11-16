using System;
using System.Threading;
using System.Threading.Tasks;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Operator
{
    // The voice Operator on the Convex backend (module "operator"). A call carries the final
    // transcript and the view snapshot taken when Call was pressed; the answer is applied,
    // pending (needs operator:confirm within 10 s) or refused, with a short line for the pink
    // bar. The effect row comes back in the answer so it can be applied before the
    // operator:activeEffects subscription catches up.
    public sealed class ConvexOperator : IOperator
    {
        public readonly IConvexClient Client;
        readonly OperatorConfig config;

        public double ServerOffsetMs { get; private set; }

        public ConvexOperator(IConvexClient client, OperatorConfig config)
        {
            Client = client;
            this.config = config;
        }

        public static double LocalNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public double ServerNowMs => LocalNowMs + ServerOffsetMs;

        void Clock(double serverNow)
        {
            if (serverNow > 0) ServerOffsetMs = serverNow - LocalNowMs;
        }

        public async Task<string> MintScribeTokenAsync(string playerId)
        {
            var json = await Client.ActionAsync("operator:mintScribeToken", JsonUtility.ToJson(new PlayerArgs { playerId = playerId }));
            var result = JsonUtility.FromJson<ScribeTokenResult>(json);
            Clock(result.serverNow);
            return result.token;
        }

        public static string Refused(string message) => string.IsNullOrWhiteSpace(message) || message.Split(' ').Length > 4 ? "Try again, man." : message;

        public async Task<OperatorResponse> CallAsync(OperatorCall call, CancellationToken cancel = default)
        {
            var text = (call.Text ?? "").Trim();
            if (text.Length == 0) return Refusal("empty", "Didn't catch that.", text);
            var args = new OperatorCallArgs
            {
                roomId = call.RoomId,
                playerId = call.PlayerId,
                transcript = text,
                view = call.View ?? new OperatorView { mode = "onFoot", timeOfDay = "day", weather = "clear", entities = new ViewEntity[0] },
                clientTimings = new ClientTimings { sttMs = call.SttMs },
            };
            var sent = LocalNowMs;
            try
            {
                var json = await Client.ActionAsync("operator:call", JsonUtility.ToJson(args), cancel);
                var result = JsonUtility.FromJson<OperatorCallResult>(json);
                Clock(result.serverNow);
                var refusal = result.refusal != null && !string.IsNullOrEmpty(result.refusal.message) ? result.refusal : null;
                var effect = result.effect != null && result.effect.IsSet ? result.effect : null;
                string line = !string.IsNullOrWhiteSpace(result.line) ? result.line : refusal != null ? Refused(refusal.message) : result.outcome == "pending" ? "Confirm for everyone?" : effect?.label ?? "Done, sugar!";
                Debug.Log($"[Operator] call \"{text}\" -> {result.outcome} {effect?.effect} {effect?.scope}:{effect?.target}{effect?.targetClass} \"{line}\" in {LocalNowMs - sent:F0} ms (server {result.timings?.totalMs:F0} ms, jev {result.timings?.jevMs:F0} ms, stt {call.SttMs} ms)");
                return new OperatorResponse { line = line, provider = "convex:" + Client.Transport, outcome = result.outcome, effect = effect, refusal = refusal, serverNow = result.serverNow, transcript = text };
            }
            catch (ConvexException e)
            {
                Debug.LogWarning($"[Operator] call failed {e.Code}: {e.Message}");
                return Refusal(e.Code.ToLowerInvariant(), e.Code == "NETWORK" ? "No signal, sorry." : e.Code == "RATE_LIMITED" ? "Slow down, caller." : Refused(e.Message), text);
            }
        }

        OperatorResponse Refusal(string reason, string message, string text) => new OperatorResponse
        {
            line = message,
            provider = "convex:" + Client.Transport,
            outcome = "refused",
            refusal = new OperatorRefusal { reason = reason, message = message },
            transcript = text,
        };

        public async Task<ActiveEffect> ConfirmAsync(string playerId, string effectId)
        {
            var json = await Client.MutationAsync("operator:confirm", JsonUtility.ToJson(new ConfirmArgs { playerId = playerId, effectId = effectId }));
            return JsonUtility.FromJson<ActiveEffect>(json);
        }

        public IDisposable WatchEffects(string roomId, Action<ActiveEffect[]> onEffects)
        {
            return Client.Subscribe("operator:activeEffects", JsonUtility.ToJson(new RoomArgs { roomId = roomId }),
                json => onEffects(JsonUtility.FromJson<ActiveEffectList>(json)?.effects ?? new ActiveEffect[0]),
                e => Debug.LogWarning("[Operator] activeEffects: " + e.Message));
        }
    }
}
