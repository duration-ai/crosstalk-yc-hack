using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dogpatch.Operator
{
    // A Convex deployment as the Operator sees it: functions by path ("operator:call"), arguments
    // and results as JSON text so JsonUtility stays the only (de)serializer in game code. Two
    // transports implement it: ConvexDotnetClient over the unofficial Convex.Client package
    // (websocket, live subscriptions) and ConvexHttpClient over Convex's public HTTP API (polled
    // subscriptions). ConvexClients.ConnectAsync picks whichever one works on this device.
    public interface IConvexClient : IDisposable
    {
        string Transport { get; }

        Task<string> ActionAsync(string path, string argsJson, CancellationToken cancel = default);

        Task<string> MutationAsync(string path, string argsJson, CancellationToken cancel = default);

        Task<string> QueryAsync(string path, string argsJson, CancellationToken cancel = default);

        // onValue gets the query's result JSON on the Unity main thread each time it changes.
        IDisposable Subscribe(string path, string argsJson, Action<string> onValue, Action<Exception> onError);
    }

    // A failed Convex function call. ConvexError data {code, message} lands in Code and Message;
    // transport failures have Code "NETWORK".
    public sealed class ConvexException : Exception
    {
        public string Code { get; }
        public string ErrorData { get; }

        public ConvexException(string code, string message, string data = null) : base(message)
        {
            Code = code;
            ErrorData = data;
        }

        [Serializable]
        class ErrorBody
        {
            public string code;
            public string message;
        }

        public static ConvexException FromError(string errorMessage, string errorDataJson)
        {
            if (!string.IsNullOrEmpty(errorDataJson) && errorDataJson.TrimStart().StartsWith("{"))
            {
                var data = UnityEngine.JsonUtility.FromJson<ErrorBody>(errorDataJson);
                if (!string.IsNullOrEmpty(data.code)) return new ConvexException(data.code, string.IsNullOrEmpty(data.message) ? errorMessage : data.message, errorDataJson);
            }
            return new ConvexException("SERVER", errorMessage ?? "Convex error", errorDataJson);
        }
    }

    public static class ConvexClients
    {
        // transport: "auto" tries Convex.Client (when it is in the build) and falls back to HTTP,
        // "dotnet" or "http" forces one. A transport that cannot reach the deployment within the
        // timeout is skipped; null means neither could.
        public static async Task<IConvexClient> ConnectAsync(string url, string transport, string probePath, string probeArgs, float timeoutSeconds)
        {
            url = url.TrimEnd('/');
#if CONVEX_DOTNET
            if (transport != "http")
            {
                var dotnet = await ConvexDotnetClient.TryConnectAsync(url, probePath, probeArgs, timeoutSeconds);
                if (dotnet != null || transport == "dotnet") return dotnet;
            }
#else
            if (transport == "dotnet")
            {
                UnityEngine.Debug.LogWarning("[Operator] Convex.Client is not in this build (CONVEX_DOTNET undefined); using HTTP");
            }
#endif
            var http = new ConvexHttpClient(url);
            try
            {
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                    await http.QueryAsync(probePath, probeArgs, cancel.Token);
                UnityEngine.Debug.Log("[Operator] Convex over HTTP: " + url);
                return http;
            }
            catch (ConvexException e) when (e.Code != "NETWORK")
            {
                UnityEngine.Debug.Log($"[Operator] Convex over HTTP: {url} (probe {probePath} answered {e.Code}: {e.Message})");
                return http;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Operator] Convex unreachable at {url}: {e.Message}");
                http.Dispose();
                return null;
            }
        }
    }
}
