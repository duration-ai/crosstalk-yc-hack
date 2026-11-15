using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Dogpatch.Backend
{
    // HTTP calls for the two avatar endpoints, keyed by the game's own player id. Used by
    // InMemoryGameBackend; errors come back as BackendException with the server's code
    // (RETAKE, DECISION_UNAVAILABLE, INVALID_ARGUMENT) or OFFLINE when nothing answered.
    public sealed class AvatarHttpClient
    {
        public const string DefaultBaseUrl = "https://rapid-gnat-965.convex.site";

        [Serializable] class AvatarEnvelope { public Avatar avatar; }
        [Serializable] class ErrorBody { public string code; public string message; }
        [Serializable] class ErrorEnvelope { public ErrorBody error; }

        readonly string baseUrl;

        public AvatarHttpClient(string baseUrl) => this.baseUrl = baseUrl.TrimEnd('/');

        // -dogpatch-avatar-url <url> or DOGPATCH_AVATAR_URL, else the deployment. "stub" or
        // "offline" means no server at all (null).
        public static string ConfiguredBaseUrl()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-dogpatch-avatar-url");
            string url = i >= 0 && i + 1 < args.Length ? args[i + 1] : Environment.GetEnvironmentVariable("DOGPATCH_AVATAR_URL");
            if (string.IsNullOrWhiteSpace(url)) return DefaultBaseUrl;
            url = url.Trim();
            return url.Equals("stub", StringComparison.OrdinalIgnoreCase) || url.Equals("offline", StringComparison.OrdinalIgnoreCase) ? null : url;
        }

        public async Task<AvatarResult> AnalyzePhotoAsync(string playerId, byte[] jpeg)
        {
            var text = await SendAsync("POST", "/api/avatar/photo?playerId=" + UnityWebRequest.EscapeURL(playerId), jpeg);
            var result = JsonUtility.FromJson<AvatarResult>(text);
            if (result?.avatar == null || result.avatar.IsEmpty) throw new BackendException(BackendErrorCodes.Internal, "photo upload returned no avatar");
            return result;
        }

        public async Task<Avatar> GetAvatarAsync(string playerId)
        {
            var text = await SendAsync("GET", "/api/avatar?playerId=" + UnityWebRequest.EscapeURL(playerId), null);
            var avatar = JsonUtility.FromJson<AvatarEnvelope>(text)?.avatar;
            return avatar == null || avatar.IsEmpty ? null : avatar;
        }

        async Task<string> SendAsync(string method, string path, byte[] jpeg)
        {
            using var request = new UnityWebRequest(baseUrl + path, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30,
            };
            if (jpeg != null)
            {
                request.uploadHandler = new UploadHandlerRaw(jpeg) { contentType = "image/jpeg" };
                request.SetRequestHeader("Content-Type", "image/jpeg");
            }
            await Send(request);
            string text = request.downloadHandler.text;
            if (request.result == UnityWebRequest.Result.ConnectionError || request.responseCode == 0)
                throw new BackendException(AvatarErrorCodes.Offline, request.error ?? "no response");
            if (request.responseCode >= 200 && request.responseCode < 300) return text;

            ErrorBody error = null;
            try { error = JsonUtility.FromJson<ErrorEnvelope>(text)?.error; } catch (ArgumentException) { }
            string code = !string.IsNullOrEmpty(error?.code) ? error.code
                : request.responseCode == 422 ? AvatarErrorCodes.Retake
                : request.responseCode == 503 ? AvatarErrorCodes.DecisionUnavailable
                : request.responseCode == 400 ? BackendErrorCodes.InvalidArgument
                : BackendErrorCodes.Internal;
            throw new BackendException(code, $"{method} {path.Split('?').First()} {request.responseCode}: {error?.message ?? text}");
        }

        public static Task Send(UnityWebRequest request)
        {
            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.TrySetResult(true);
            return done.Task;
        }
    }
}
