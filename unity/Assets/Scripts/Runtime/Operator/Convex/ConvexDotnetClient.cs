#if CONVEX_DOTNET
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConvexSdk = Convex.Client;

namespace Dogpatch.Operator
{
    // Convex over the unofficial Convex.Client package (Assets/Plugins/ConvexClient, compiled in
    // when Assets/csc.rsp defines CONVEX_DOTNET): one websocket for functions and live
    // subscriptions. Values cross as System.Text.Json JsonElement and leave as raw JSON text.
    public sealed class ConvexDotnetClient : IConvexClient
    {
        readonly ConvexSdk.ConvexClient client;
        readonly SynchronizationContext main;

        ConvexDotnetClient(ConvexSdk.ConvexClient client, SynchronizationContext main)
        {
            this.client = client;
            this.main = main;
        }

        public string Transport => "dotnet";

        public static async Task<IConvexClient> TryConnectAsync(string url, string probePath, string probeArgs, float timeoutSeconds)
        {
            string step = "construct";
            ConvexSdk.ConvexClient client = null;
            try
            {
                client = new ConvexSdk.ConvexClient(url);
                step = "connect";
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    var connect = client.EnsureConnectedAsync(cancel.Token);
                    if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) != connect) throw new TimeoutException("connect timed out");
                    await connect;
                    step = "query " + probePath;
                    var wrapper = new ConvexDotnetClient(client, SynchronizationContext.Current);
                    var probe = wrapper.QueryAsync(probePath, probeArgs, cancel.Token);
                    if (await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) != probe) throw new TimeoutException("probe query timed out");
                    try { await probe; }
                    catch (ConvexException e) when (e.Code != "NETWORK") { }
                    UnityEngine.Debug.Log($"[Operator] Convex over Convex.Client websocket: {url} ({client.ConnectionState})");
                    return wrapper;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Operator] Convex.Client failed at {step}: {e.GetType().Name}: {e.Message}");
                client?.Dispose();
                return null;
            }
        }

        // Convex.Client does not serialize a JsonElement as the object it holds (the server sees an
        // empty object), so arguments go over as plain dictionaries, lists, strings, bools and
        // doubles; every number is a double so v.number() validators accept it.
        static Dictionary<string, object> Args(string argsJson)
        {
            using (var doc = JsonDocument.Parse(string.IsNullOrEmpty(argsJson) ? "{}" : argsJson))
                return (Dictionary<string, object>)Plain(doc.RootElement);
        }

        static object Plain(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) map[p.Name] = Plain(p.Value);
                    return map;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in e.EnumerateArray()) list.Add(Plain(item));
                    return list;
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number: return e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }

        // The package reports a ConvexError as "...Server Error\nUncaught ConvexError: {data}\n at ...".
        static ConvexException Wrap(Exception e)
        {
            var inner = e is AggregateException agg ? agg.GetBaseException() : e;
            string text = inner.Message ?? "";
            int marker = text.IndexOf("ConvexError:", StringComparison.Ordinal);
            int brace = marker >= 0 ? text.IndexOf('{', marker) : -1;
            int end = brace >= 0 ? text.IndexOf('\n', brace) : -1;
            if (brace >= 0)
            {
                var data = (end > 0 ? text.Substring(brace, end - brace) : text.Substring(brace)).Trim();
                data = data.Substring(0, data.LastIndexOf('}') + 1);
                if (!string.IsNullOrEmpty(JsonText.Unquote(JsonText.Field(data, "code")))) return ConvexException.FromError(text, data);
            }
            return new ConvexException(inner is TimeoutException || inner is System.Net.WebSockets.WebSocketException ? "NETWORK" : "SERVER", text);
        }

        public async Task<string> ActionAsync(string path, string argsJson, CancellationToken cancel = default)
        {
            try { return (await client.Action<JsonElement>(path).WithArgs(Args(argsJson)).ExecuteAsync(cancel)).GetRawText(); }
            catch (Exception e) when (!(e is OperationCanceledException)) { throw Wrap(e); }
        }

        public async Task<string> MutationAsync(string path, string argsJson, CancellationToken cancel = default)
        {
            try { return (await client.Mutate<JsonElement>(path).WithArgs(Args(argsJson)).ExecuteAsync(cancel)).GetRawText(); }
            catch (Exception e) when (!(e is OperationCanceledException)) { throw Wrap(e); }
        }

        public async Task<string> QueryAsync(string path, string argsJson, CancellationToken cancel = default)
        {
            try { return (await client.Query<JsonElement>(path).WithArgs(Args(argsJson)).ExecuteAsync(cancel)).GetRawText(); }
            catch (Exception e) when (!(e is OperationCanceledException)) { throw Wrap(e); }
        }

        public IDisposable Subscribe(string path, string argsJson, Action<string> onValue, Action<Exception> onError)
        {
            var observer = new Observer(this, onValue, onError);
            return client.Observe<JsonElement, Dictionary<string, object>>(path, Args(argsJson)).Subscribe(observer);
        }

        void Post(Action action)
        {
            if (main == null) action();
            else main.Post(_ => action(), null);
        }

        sealed class Observer : IObserver<JsonElement>
        {
            readonly ConvexDotnetClient owner;
            readonly Action<string> onValue;
            readonly Action<Exception> onError;

            public Observer(ConvexDotnetClient owner, Action<string> onValue, Action<Exception> onError)
            {
                this.owner = owner;
                this.onValue = onValue;
                this.onError = onError;
            }

            public void OnNext(JsonElement value)
            {
                string raw = value.GetRawText();
                owner.Post(() => onValue(raw));
            }

            public void OnError(Exception error) => owner.Post(() => onError?.Invoke(Wrap(error)));

            public void OnCompleted() { }
        }

        public void Dispose() => client.Dispose();
    }
}
#endif
