using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Dogpatch.Operator
{
    // Convex over its public HTTP API: POST {url}/api/action|mutation|query with
    // {path, args, format:"json"}. Subscriptions poll the query every PollSeconds and report only
    // changed results. Calls must start on the Unity main thread (UnityWebRequest).
    public sealed class ConvexHttpClient : IConvexClient
    {
        public float PollSeconds = 0.25f;

        readonly string url;
        bool disposed;

        public ConvexHttpClient(string url) => this.url = url.TrimEnd('/');

        public string Transport => "http";

        public Task<string> ActionAsync(string path, string argsJson, CancellationToken cancel = default) => PostAsync("action", path, argsJson, cancel);

        public Task<string> MutationAsync(string path, string argsJson, CancellationToken cancel = default) => PostAsync("mutation", path, argsJson, cancel);

        public Task<string> QueryAsync(string path, string argsJson, CancellationToken cancel = default) => PostAsync("query", path, argsJson, cancel);

        public static string Envelope(string path, string argsJson) =>
            "{\"path\":" + JsonText.Quote(path) + ",\"args\":" + (string.IsNullOrEmpty(argsJson) ? "{}" : argsJson) + ",\"format\":\"json\"}";

        // Turns a Convex HTTP API response body into the function's value, or throws its error.
        public static string Unwrap(string body, long status, string transportError)
        {
            string state = JsonText.Unquote(JsonText.Field(body, "status"));
            if (state == "success") return JsonText.Field(body, "value");
            if (state == "error")
                throw ConvexException.FromError(JsonText.Unquote(JsonText.Field(body, "errorMessage")), JsonText.Field(body, "errorData"));
            throw new ConvexException("NETWORK", $"HTTP {status}: {transportError ?? body}");
        }

        async Task<string> PostAsync(string kind, string path, string argsJson, CancellationToken cancel)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ConvexHttpClient));
            using (var request = new UnityWebRequest(url + "/api/" + kind, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Envelope(path, argsJson)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;
                var done = new TaskCompletionSource<bool>();
                var op = request.SendWebRequest();
                op.completed += _ => done.TrySetResult(true);
                using (cancel.Register(() => { request.Abort(); done.TrySetCanceled(); }))
                    await done.Task;
                string body = request.downloadHandler?.text;
                bool transportFailed = request.result == UnityWebRequest.Result.ConnectionError || string.IsNullOrEmpty(body);
                return Unwrap(body, request.responseCode, transportFailed ? request.error ?? "no response" : null);
            }
        }

        public IDisposable Subscribe(string path, string argsJson, Action<string> onValue, Action<Exception> onError)
        {
            var poll = new Poll();
            _ = PollAsync(poll, path, argsJson, onValue, onError);
            return poll;
        }

        sealed class Poll : IDisposable
        {
            public bool Stopped;
            public void Dispose() => Stopped = true;
        }

        async Task PollAsync(Poll poll, string path, string argsJson, Action<string> onValue, Action<Exception> onError)
        {
            string last = null;
            int failures = 0;
            while (!poll.Stopped && !disposed)
            {
                try
                {
                    var value = await QueryAsync(path, argsJson);
                    failures = 0;
                    if (!poll.Stopped && value != last)
                    {
                        last = value;
                        onValue(value);
                    }
                }
                catch (Exception e)
                {
                    if (failures++ % 20 == 0) onError?.Invoke(e);
                }
                await Task.Delay(TimeSpan.FromSeconds(failures > 0 ? Mathf.Min(PollSeconds * (1 + failures), 3f) : PollSeconds));
            }
        }

        public void Dispose() => disposed = true;
    }
}
