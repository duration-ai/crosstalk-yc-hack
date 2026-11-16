using UnityEngine;
using UnityEngine.Profiling;

namespace Dogpatch.World
{
    public static class MemoryStages
    {
        static long last;

        public static void Mark(string stage)
        {
            long now = Profiler.GetTotalAllocatedMemoryLong();
            Debug.Log($"[Memory] {stage}: {now / 1048576} MB allocated (+{(now - last) / 1048576} MB), mono {Profiler.GetMonoUsedSizeLong() / 1048576} MB");
            last = now;
        }
    }
}
