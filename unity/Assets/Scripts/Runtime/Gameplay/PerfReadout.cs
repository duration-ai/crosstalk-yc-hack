using System;
using Unity.Profiling;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Debug performance readout: FPS, frame time, draw calls, triangles and world build time in a
    // corner, off by default. Launch with -perf (or --perf) or tap with three fingers to toggle.
    // While on, it logs a one-line summary every 5 s through Debug.Log, which reaches Xcode and
    // "devicectl device process launch --console" on a physical phone.
    public sealed class PerfReadout : MonoBehaviour
    {
        const float LogSeconds = 5f;
        const float RefreshSeconds = 0.5f;

        public GameController Game;
        public static float WorldBuildSeconds;

        bool on;
        bool gestureUsed;
        ProfilerRecorder drawCalls, triangles;
        int windowFrames, logFrames;
        float windowTime, logTime, logWorst, nextRefresh;
        string text = "";
        GUIStyle style;

        void Awake()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg == "-perf" || arg == "--perf") on = true;
            if (on) Enable();
        }

        void Enable()
        {
            on = true;
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            windowFrames = logFrames = 0;
            windowTime = logTime = logWorst = 0f;
            Debug.Log("[Perf] readout on");
        }

        void Disable()
        {
            on = false;
            drawCalls.Dispose();
            triangles.Dispose();
            Debug.Log("[Perf] readout off");
        }

        void OnDestroy()
        {
            if (on) Disable();
        }

        void Update()
        {
            if (Input.touchCount == 0) gestureUsed = false;
            else if (Input.touchCount == 3 && !gestureUsed)
            {
                gestureUsed = true;
                if (on) Disable();
                else Enable();
            }
            if (!on) return;

            float dt = Time.unscaledDeltaTime;
            windowFrames++;
            windowTime += dt;
            logFrames++;
            logTime += dt;
            logWorst = Mathf.Max(logWorst, dt);

            if (Time.unscaledTime >= nextRefresh && windowTime > 0f)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                float fps = windowFrames / windowTime;
                text = $"{fps:F0} FPS  {1000f * windowTime / windowFrames:F1} ms\n{Count(drawCalls)} draws  {Count(triangles)} tris\nworld {WorldBuildSeconds:F1} s";
                windowFrames = 0;
                windowTime = 0f;
            }

            if (logTime >= LogSeconds)
            {
                var p = Game != null && Game.View != null ? Game.View.transform.position : Vector3.zero;
                string street = Game != null ? Game.StreetName : "";
                Debug.Log($"[Perf] fps={logFrames / logTime:F1} ms={1000f * logTime / logFrames:F1} worstMs={1000f * logWorst:F1} draws={Count(drawCalls)} tris={Count(triangles)} worldBuildS={WorldBuildSeconds:F2} cam=({p.x:F0},{p.z:F0}) street={street} device={SystemInfo.deviceModel} res={Screen.width}x{Screen.height}");
                logFrames = 0;
                logTime = logWorst = 0f;
            }
        }

        static string Count(ProfilerRecorder r)
        {
            if (!r.Valid) return "n/a";
            long v = r.LastValue;
            return v >= 1_000_000 ? $"{v / 1_000_000f:F2}M" : v >= 1000 ? $"{v / 1000f:F0}k" : v.ToString();
        }

        void OnGUI()
        {
            if (!on) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.016f), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            var safe = Screen.safeArea;
            float w = Screen.width * 0.5f, h = style.fontSize * 4.5f;
            var rect = new Rect(safe.xMax - w - 12f, Screen.height - safe.yMax + Screen.height * 0.13f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }
    }
}
