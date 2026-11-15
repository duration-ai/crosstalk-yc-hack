using System;
using UnityEngine;

namespace Dogpatch.Gameplay
{
    // Always-on hitch log for phone playtests. Any frame over 40 ms logs one line:
    //   [Hitch] <ms> gc=<collections this frame> alloc=<KB managed heap growth since last frame>
    //   streaming=<city chunk streams running> net=<socket messages handled this frame>
    //   fx=<particle systems playing> rb=<awake rigidbodies>
    // and every 10 s one summary with p50/p99/max frame time, hitch count and GC collections.
    // The per-frame cost is a few counters; the fx/rb scan only runs on a hitch frame.
    public sealed class HitchLogger : MonoBehaviour
    {
        const float HitchMs = 40f;
        const float SummarySeconds = 10f;

        // Set by CityRegions while it streams the Mission Bay, South Beach and SoMa chunks.
        public static int StreamingChunks;
        // Counted by ConvexSocket for every message it handles on the main thread.
        public static int NetMessages;

        readonly float[] frames = new float[2048];
        int frameCount, hitches, lastGc, summaryGc, lastNet;
        long lastHeap;
        float summaryStart, worst;

        void OnEnable()
        {
            lastGc = summaryGc = GC.CollectionCount(0);
            lastHeap = GC.GetTotalMemory(false);
            lastNet = NetMessages;
            summaryStart = Time.realtimeSinceStartup;
        }

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            int gc = GC.CollectionCount(0);
            long heap = GC.GetTotalMemory(false);
            int net = NetMessages;

            if (frameCount < frames.Length) frames[frameCount++] = ms;
            worst = Mathf.Max(worst, ms);
            if (ms > HitchMs)
            {
                hitches++;
                int fx = 0, rb = 0;
                foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    if (ps.isPlaying) fx++;
                foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                    if (!body.isKinematic && !body.IsSleeping()) rb++;
                string alloc = gc == lastGc ? ((heap - lastHeap) / 1024).ToString() : "?";
                Debug.Log($"[Hitch] {ms:F0} gc={gc - lastGc} alloc={alloc} streaming={StreamingChunks} net={net - lastNet} fx={fx} rb={rb} t={Time.realtimeSinceStartup:F1}");
            }
            lastGc = gc;
            lastHeap = heap;
            lastNet = net;

            float elapsed = Time.realtimeSinceStartup - summaryStart;
            if (elapsed < SummarySeconds || frameCount == 0) return;
            Array.Sort(frames, 0, frameCount);
            float p50 = frames[frameCount / 2], p99 = frames[Mathf.Min(frameCount - 1, (int)(frameCount * 0.99f))];
            int collections = gc - summaryGc;
            Debug.Log($"[Hitch] summary {elapsed:F0}s: frames={frameCount} p50={p50:F1} p99={p99:F1} max={worst:F0} hitches={hitches} gc={collections} ({collections * 60f / elapsed:F1}/min) heap={heap / (1024 * 1024)}MB");
            frameCount = hitches = 0;
            worst = 0f;
            summaryGc = gc;
            summaryStart = Time.realtimeSinceStartup;
        }
    }
}
