using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dogpatch.Operator;
using UnityEngine;

namespace Dogpatch.Backend
{
    // A minimal client for Convex's sync protocol over one WebSocket, the wire the convex npm client
    // speaks (convex/src/browser/sync/protocol.ts): Connect, ModifyQuerySet to subscribe, Transition
    // messages carrying QueryUpdated values the server pushes whenever a subscribed query's result
    // changes, Mutation and MutationResponse, and server Pings that only keep the link alive.
    // Frames are read off the main thread, so a low frame rate never backs messages up, and every
    // message is handled on the main thread through Unity's synchronization context, so
    // QueryUpdated and mutation results arrive where game code can use them. The socket reconnects
    // with backoff and resubscribes; mutations in flight when it drops fail with UNAVAILABLE so the
    // caller can fall back to HTTP.
    public sealed class ConvexSocket : IDisposable
    {
        public const string Unavailable = "UNAVAILABLE";
        const string ClientVersion = "1.50.0";
        const int ConnectTimeoutMs = 5000;
        const int InactivityMs = 30000;

        static readonly Regex IntegerFloats = new Regex(@"(?<=[:\[,]-?\d+)\.0(?=[,\]\}])", RegexOptions.Compiled);
        static readonly Regex ErrorCode = new Regex("\\\\?\"code\\\\?\":\\\\?\"([A-Z_]+)");

        readonly Uri uri;
        readonly SynchronizationContext main = SynchronizationContext.Current;
        readonly string sessionId = Guid.NewGuid().ToString();
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly Dictionary<int, string> queries = new Dictionary<int, string>();
        readonly Dictionary<int, TaskCompletionSource<string>> pending = new Dictionary<int, TaskCompletionSource<string>>();
        readonly Queue<string> outbox = new Queue<string>();
        ClientWebSocket ws;
        int nextQueryId, nextRequestId, querySetVersion, connectionCount;
        bool sending;
        string lastCloseReason;
        StringBuilder chunks;

        public ConvexSocket(string deploymentUrl)
        {
            uri = SyncUri(deploymentUrl);
            _ = RunAsync();
        }

        public bool Connected { get; private set; }

        // A subscribed query's new value as raw JSON, with its query id.
        public event Action<int, string> QueryUpdated;

        // https://<name>.convex.site (HTTP actions) or .convex.cloud -> wss://<name>.convex.cloud/api/<v>/sync
        public static Uri SyncUri(string deploymentUrl)
        {
            var u = new Uri(deploymentUrl);
            string host = u.Host.EndsWith(".convex.site", StringComparison.Ordinal) ? u.Host.Substring(0, u.Host.Length - 4) + "cloud" : u.Host;
            string port = u.IsDefaultPort ? "" : ":" + u.Port;
            return new Uri((u.Scheme == "http" ? "ws" : "wss") + "://" + host + port + "/api/" + ClientVersion + "/sync");
        }

        // Convex sends float64 numbers with a trailing .0 (1794780612965.0), which JsonUtility won't read
        // into a long; integers go back to plain digits.
        public static string Plain(string json) => json == null ? null : IntegerFloats.Replace(json, "");

        public int Subscribe(string udfPath, string argsJson)
        {
            int id = nextQueryId++;
            string add = "{\"type\":\"Add\",\"queryId\":" + id + ",\"udfPath\":" + JsonText.Quote(udfPath) + ",\"args\":[" + argsJson + "]}";
            queries[id] = add;
            if (Connected) ModifyQuerySet(add);
            return id;
        }

        public void Unsubscribe(int queryId)
        {
            if (!queries.Remove(queryId)) return;
            if (Connected) ModifyQuerySet("{\"type\":\"Remove\",\"queryId\":" + queryId + "}");
        }

        public async Task<string> MutationAsync(string udfPath, string argsJson, float timeoutSeconds)
        {
            if (!Connected) throw new BackendException(Unavailable, "socket not connected");
            int id = nextRequestId++;
            var done = new TaskCompletionSource<string>();
            pending[id] = done;
            Send("{\"type\":\"Mutation\",\"requestId\":" + id + ",\"udfPath\":" + JsonText.Quote(udfPath) + ",\"args\":[" + argsJson + "]}");
            if (await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) != done.Task)
            {
                pending.Remove(id);
                throw new BackendException(Unavailable, udfPath + " timed out on the socket");
            }
            return await done.Task;
        }

        public void Dispose()
        {
            life.Cancel();
            ws?.Abort();
            Close("disposed");
        }

        void ModifyQuerySet(string modifications)
        {
            Send("{\"type\":\"ModifyQuerySet\",\"baseVersion\":" + querySetVersion + ",\"newVersion\":" + (querySetVersion + 1) + ",\"modifications\":[" + modifications + "]}");
            querySetVersion++;
        }

        void Send(string text)
        {
            outbox.Enqueue(text);
            if (!sending) _ = PumpAsync();
        }

        async Task PumpAsync()
        {
            var socket = ws;
            sending = true;
            try
            {
                while (outbox.Count > 0 && socket != null && socket == ws && socket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(outbox.Dequeue());
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, life.Token);
                }
            }
            catch (Exception e)
            {
                if (socket == ws) socket?.Abort();
                lastCloseReason = "send: " + e.Message;
            }
            finally
            {
                sending = false;
                if (outbox.Count > 0 && ws != socket && ws != null && ws.State == WebSocketState.Open) _ = PumpAsync();
            }
        }

        async Task RunAsync()
        {
            int failures = 0;
            while (!life.IsCancellationRequested)
            {
                var socket = new ClientWebSocket();
                ws = socket;
                try
                {
                    using (var connect = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                    {
                        connect.CancelAfter(ConnectTimeoutMs);
                        await socket.ConnectAsync(uri, connect.Token);
                    }
                    outbox.Clear();
                    querySetVersion = 0;
                    Send("{\"type\":\"Connect\",\"sessionId\":\"" + sessionId + "\",\"connectionCount\":" + connectionCount +
                         ",\"lastCloseReason\":" + (lastCloseReason == null ? "null" : JsonText.Quote(lastCloseReason)) +
                         ",\"clientTs\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "}");
                    if (queries.Count > 0) ModifyQuerySet(string.Join(",", queries.Values));
                    Connected = true;
                    failures = 0;
                    Debug.Log($"[ConvexSocket] connected to {uri} ({queries.Count} subscriptions)");
                    lastCloseReason = await ReceiveAsync(socket);
                }
                catch (Exception e)
                {
                    lastCloseReason = e.GetType().Name + ": " + e.Message;
                }
                socket.Abort();
                socket.Dispose();
                Close(lastCloseReason);
                if (life.IsCancellationRequested) return;
                connectionCount++;
                failures++;
                float wait = Mathf.Min(0.5f * Mathf.Pow(2f, failures - 1), 4f) * UnityEngine.Random.Range(0.8f, 1.2f);
                if (failures <= 3 || failures % 10 == 0) Debug.LogWarning($"[ConvexSocket] closed ({lastCloseReason}); reconnecting in {wait:F1}s");
                try { await Task.Delay(TimeSpan.FromSeconds(wait), life.Token); }
                catch (OperationCanceledException) { return; }
            }
        }

        // Reads whole messages on a worker thread and posts each one to the main thread; returns why the
        // socket closed.
        async Task<string> ReceiveAsync(ClientWebSocket socket)
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            try
            {
                while (true)
                {
                    WebSocketReceiveResult result;
                    using (var idle = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                    {
                        idle.CancelAfter(InactivityMs);
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), idle.Token).ConfigureAwait(false);
                    }
                    if (result.MessageType == WebSocketMessageType.Close) return $"{(int?)result.CloseStatus} {result.CloseStatusDescription}";
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    main.Post(_ =>
                    {
                        if (socket != ws) return;
                        try { Handle(text); }
                        catch (Exception e) { BadMessage(e); }
                    }, null);
                }
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
        }

        void Close(string reason)
        {
            bool was = Connected;
            Connected = false;
            chunks = null;
            foreach (var done in pending.Values) done.TrySetException(new BackendException(Unavailable, "socket closed: " + reason));
            pending.Clear();
            if (was) Debug.Log("[ConvexSocket] disconnected: " + reason);
        }

        void Handle(string text)
        {
            switch (JsonText.Unquote(JsonText.Field(text, "type")))
            {
                case "Transition":
                    Transition(text);
                    break;
                case "TransitionChunk":
                    if (chunks == null) chunks = new StringBuilder();
                    chunks.Append(JsonText.Unquote(JsonText.Field(text, "chunk")));
                    if (Int(JsonText.Field(text, "partNumber")) + 1 < Int(JsonText.Field(text, "totalParts"))) break;
                    string whole = chunks.ToString();
                    chunks = null;
                    Transition(whole);
                    break;
                case "MutationResponse":
                    int id = Int(JsonText.Field(text, "requestId"));
                    if (!pending.TryGetValue(id, out var done)) break;
                    pending.Remove(id);
                    if (JsonText.Field(text, "success") == "true")
                    {
                        done.TrySetResult(Plain(JsonText.Field(text, "result")));
                        break;
                    }
                    string error = JsonText.Unquote(JsonText.Field(text, "result")) ?? "";
                    var code = ErrorCode.Match((JsonText.Field(text, "errorData") ?? "") + error);
                    done.TrySetException(new BackendException(code.Success ? code.Groups[1].Value : BackendErrorCodes.Internal, error));
                    break;
                case "FatalError":
                case "AuthError":
                    Debug.LogWarning("[ConvexSocket] " + text);
                    ws?.Abort();
                    break;
            }
        }

        int badMessages;

        // A message the game cannot handle is logged in full the first few times, then only every
        // thousandth: one broken push repeats with every push (~30/s), and each log costs a frame.
        void BadMessage(Exception e)
        {
            badMessages++;
            if (badMessages <= 3 || badMessages % 1000 == 0) Debug.LogWarning($"[ConvexSocket] bad message ({badMessages} so far): {e}");
        }

        void Transition(string text)
        {
            foreach (var change in JsonText.Elements(JsonText.Field(text, "modifications")))
            {
                string type = JsonText.Unquote(JsonText.Field(change, "type"));
                int queryId = Int(JsonText.Field(change, "queryId"));
                if (type == "QueryUpdated") QueryUpdated?.Invoke(queryId, Plain(JsonText.Field(change, "value")));
                else if (type == "QueryFailed") Debug.LogWarning($"[ConvexSocket] query {queryId} failed: {JsonText.Unquote(JsonText.Field(change, "errorMessage"))}");
            }
        }

        static int Int(string raw) => raw == null ? -1 : (int)double.Parse(raw, CultureInfo.InvariantCulture);
    }
}
