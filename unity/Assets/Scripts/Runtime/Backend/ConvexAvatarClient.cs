using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Dogpatch.Backend
{
    // HTTP client for just the avatar endpoints of the Convex deployment. The rest of the game
    // still runs on InMemoryGameBackend, so this client mints its own Convex player
    // (POST /api/players) the first time and keeps the id in PlayerPrefs.
    public sealed class ConvexAvatarClient : IAvatarBackend
    {
        public const string DefaultBaseUrl = "https://rapid-gnat-965.convex.site";
        const string PlayerIdKey = "dogpatch.convexPlayerId";

        [Serializable] class PlayerEnvelope { public Player player; }
        [Serializable] class AvatarEnvelope { public PlayerAvatar avatar; }
        [Serializable] class ErrorBody { public string code; public string message; }
        [Serializable] class ErrorEnvelope { public ErrorBody error; }
        [Serializable] class CreatePlayer { public string displayName; }

        readonly string baseUrl;

        public ConvexAvatarClient(string baseUrl = DefaultBaseUrl) => this.baseUrl = baseUrl.TrimEnd('/');

        public static string StoredPlayerId => PlayerPrefs.GetString(PlayerIdKey, "");

        public static void ForgetPlayer()
        {
            PlayerPrefs.DeleteKey(PlayerIdKey);
            PlayerPrefs.Save();
        }

        public async Task<string> EnsurePlayerIdAsync(string displayName = "Dogpatch Driver")
        {
            string id = StoredPlayerId;
            if (!string.IsNullOrEmpty(id)) return id;
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new CreatePlayer { displayName = displayName }));
            var text = await SendAsync("POST", "/api/players", body, "application/json");
            id = JsonUtility.FromJson<PlayerEnvelope>(text)?.player?.id;
            if (string.IsNullOrEmpty(id)) throw new BackendException(BackendErrorCodes.Internal, "POST /api/players returned no id");
            PlayerPrefs.SetString(PlayerIdKey, id);
            PlayerPrefs.Save();
            return id;
        }

        public async Task<AvatarUploadResult> UploadAvatarPhotoAsync(string playerId, byte[] jpeg)
        {
            var text = await SendAsync("POST", "/api/avatar/photo?playerId=" + UnityWebRequest.EscapeURL(playerId), jpeg, "image/jpeg");
            var result = JsonUtility.FromJson<AvatarUploadResult>(text);
            if (result?.avatar == null || result.avatar.IsEmpty) throw new BackendException(BackendErrorCodes.Internal, "upload returned no avatar");
            return result;
        }

        public async Task<PlayerAvatar> GetAvatarAsync(string playerId)
        {
            var text = await SendAsync("GET", "/api/avatar?playerId=" + UnityWebRequest.EscapeURL(playerId), null, null);
            var avatar = JsonUtility.FromJson<AvatarEnvelope>(text)?.avatar;
            return avatar == null || avatar.IsEmpty ? null : avatar;
        }

        async Task<string> SendAsync(string method, string path, byte[] body, string contentType)
        {
            using var request = new UnityWebRequest(baseUrl + path, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 45,
            };
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(body) { contentType = contentType };
                request.SetRequestHeader("Content-Type", contentType);
            }
            await Send(request);
            string text = request.downloadHandler.text;
            if (request.result == UnityWebRequest.Result.ConnectionError || request.responseCode == 0)
                throw new BackendException(AvatarErrorCodes.Offline, request.error ?? "no response");
            if (request.responseCode >= 200 && request.responseCode < 300) return text;

            ErrorBody error = null;
            try { error = JsonUtility.FromJson<ErrorEnvelope>(text)?.error; } catch (ArgumentException) { }
            string code = !string.IsNullOrEmpty(error?.code) ? error.code
                : request.responseCode == 404 ? BackendErrorCodes.NotFound
                : request.responseCode == 400 ? BackendErrorCodes.InvalidArgument
                : BackendErrorCodes.Internal;
            throw new BackendException(code, $"{method} {path} {request.responseCode}: {error?.message ?? text}");
        }

        public static Task Send(UnityWebRequest request)
        {
            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.TrySetResult(true);
            return done.Task;
        }
    }
}
