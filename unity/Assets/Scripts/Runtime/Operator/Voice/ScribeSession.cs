using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Dogpatch.Operator.Voice
{
    // One ElevenLabs Scribe realtime session (wss://api.elevenlabs.io/v1/speech-to-text/realtime)
    // on a single-use token minted by the backend. Audio goes up as input_audio_chunk messages of
    // base64 16 kHz mono int16; partial_transcript and committed_transcript come back. Started on
    // the Unity main thread, so its events land there too.
    public sealed class ScribeSession : IDisposable
    {
        public const int SampleRate = 16000;

        readonly ClientWebSocket socket = new ClientWebSocket();
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly Queue<string> outbox = new Queue<string>();
        bool pumping;
        TaskCompletionSource<string> committed;

        public string LastPartial { get; private set; } = "";
        public string Error { get; private set; }
        public bool IsOpen => socket.State == WebSocketState.Open && Error == null;

        public event Action<string> Partial;

        public static string Url(OperatorConfig config, string token)
        {
            var sb = new StringBuilder("wss://api.elevenlabs.io/v1/speech-to-text/realtime");
            sb.Append("?model_id=").Append(Uri.EscapeDataString(config.sttModel));
            sb.Append("&token=").Append(Uri.EscapeDataString(token));
            sb.Append("&audio_format=pcm_").Append(SampleRate);
            sb.Append("&commit_strategy=").Append(Uri.EscapeDataString(config.commitStrategy));
            if (!string.IsNullOrEmpty(config.language)) sb.Append("&language_code=").Append(Uri.EscapeDataString(config.language));
            if (config.keyterms != null)
                foreach (var term in config.keyterms)
                    if (!string.IsNullOrWhiteSpace(term)) sb.Append("&keyterms=").Append(Uri.EscapeDataString(term));
            return sb.ToString();
        }

        public static string Chunk(short[] pcm, int offset, int count, bool commit)
        {
            var bytes = new byte[count * 2];
            Buffer.BlockCopy(pcm, offset * 2, bytes, 0, count * 2);
            return "{\"message_type\":\"input_audio_chunk\",\"audio_base_64\":\"" + Convert.ToBase64String(bytes) +
                   "\",\"commit\":" + (commit ? "true" : "false") + ",\"sample_rate\":" + SampleRate + "}";
        }

        public async Task OpenAsync(OperatorConfig config, string token, CancellationToken cancel)
        {
            var url = Url(config, token);
            await socket.ConnectAsync(new Uri(url), cancel);
            _ = ReceiveLoop();
        }

        public void Send(short[] pcm, int count, bool commit)
        {
            if (commit) committed = new TaskCompletionSource<string>();
            outbox.Enqueue(Chunk(pcm, 0, count, commit));
            if (!pumping) _ = Pump();
        }

        async Task Pump()
        {
            pumping = true;
            try
            {
                while (outbox.Count > 0 && socket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(outbox.Dequeue());
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, life.Token);
                }
            }
            catch (Exception e)
            {
                Fail("send: " + e.Message);
            }
            finally
            {
                pumping = false;
            }
        }

        // After a commit: the committed transcript, or the last partial once waitMs passes.
        public async Task<string> FinalAsync(int waitMs)
        {
            var pending = committed;
            if (pending != null && Error == null)
            {
                var done = await Task.WhenAny(pending.Task, Task.Delay(waitMs));
                if (done == pending.Task && !string.IsNullOrWhiteSpace(pending.Task.Result)) return pending.Task.Result.Trim();
            }
            return LastPartial.Trim();
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[16 * 1024];
            var message = new StringBuilder();
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), life.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (Error == null && committed?.Task.IsCompleted == false) Fail($"closed {result.CloseStatus}: {result.CloseStatusDescription}");
                        break;
                    }
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;
                    Handle(message.ToString());
                    message.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Fail("receive: " + e.Message);
            }
        }

        void Handle(string json)
        {
            string type = JsonText.Unquote(JsonText.Field(json, "message_type"));
            string text = JsonText.Unquote(JsonText.Field(json, "text")) ?? "";
            switch (type)
            {
                case "session_started":
                    Debug.Log("[Operator] scribe session " + JsonText.Unquote(JsonText.Field(json, "session_id")));
                    break;
                case "partial_transcript":
                    if (text.Length == 0) break;
                    LastPartial = text;
                    Partial?.Invoke(text);
                    break;
                case "committed_transcript":
                    if (text.Length > 0) LastPartial = text;
                    committed?.TrySetResult(text);
                    break;
                case "committed_transcript_with_timestamps":
                    break;
                default:
                    var error = JsonText.Unquote(JsonText.Field(json, "error"));
                    if (error != null || type == "error" || type == "auth_error") Fail(type + ": " + error);
                    break;
            }
        }

        void Fail(string why)
        {
            if (Error != null) return;
            Error = why;
            Debug.LogWarning("[Operator] scribe " + why);
            committed?.TrySetResult("");
        }

        public void Dispose()
        {
            life.Cancel();
            if (socket.State == WebSocketState.Open)
                _ = socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ContinueWith(_ => socket.Dispose());
            else socket.Dispose();
        }
    }
}
