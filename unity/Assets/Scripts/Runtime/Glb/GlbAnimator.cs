using UnityEngine;

namespace Dogpatch.Glb
{
    // Plays the rotation/translation clips stored in a GLB on the instantiated joint transforms,
    // with a short crossfade when the clip changes.
    public sealed class GlbAnimator : MonoBehaviour
    {
        const float FadeDuration = 0.2f;

        GlbAsset asset;
        Transform[] nodes;
        GlbClip current;
        GlbClip previous;
        float time;
        float previousTime;
        float fade = 1f;

        public float Speed = 1f;

        public void Bind(GlbAsset glb, Transform[] transforms)
        {
            asset = glb;
            nodes = transforms;
            if (glb.Clips.Length > 0) current = glb.Clips[0];
        }

        public bool Has(string clipName)
        {
            if (asset == null) return false;
            foreach (var clip in asset.Clips) if (clip.Name == clipName) return true;
            return false;
        }

        public void Play(string clipName)
        {
            if (asset == null || (current != null && current.Name == clipName)) return;
            foreach (var clip in asset.Clips)
            {
                if (clip.Name != clipName) continue;
                previous = current;
                previousTime = time;
                current = clip;
                time = 0f;
                fade = previous == null ? 1f : 0f;
                return;
            }
        }

        void LateUpdate()
        {
            if (current == null) return;
            time = (time + Time.deltaTime * Speed) % current.Length;
            if (previous != null) previousTime = (previousTime + Time.deltaTime * Speed) % previous.Length;
            fade = Mathf.Min(1f, fade + Time.deltaTime / FadeDuration);

            Apply(current, time, 1f);
            if (previous != null && fade < 1f) Apply(previous, previousTime, 1f - fade);
        }

        void Apply(GlbClip clip, float t, float weight)
        {
            foreach (var channel in clip.Channels)
            {
                var target = nodes[channel.Node];
                if (target == null) continue;
                var value = Sample(channel, t);
                if (channel.IsRotation)
                {
                    var q = new Quaternion(value.x, value.y, value.z, value.w);
                    target.localRotation = weight >= 1f ? q : Quaternion.Slerp(q, target.localRotation, 1f - weight);
                }
                else
                {
                    var p = new Vector3(value.x, value.y, value.z);
                    target.localPosition = weight >= 1f ? p : Vector3.Lerp(p, target.localPosition, 1f - weight);
                }
            }
        }

        static Vector4 Sample(GlbChannel channel, float t)
        {
            var times = channel.Times;
            if (times.Length == 1 || t <= times[0]) return channel.Values[0];
            int last = times.Length - 1;
            if (t >= times[last]) return channel.Values[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= t) lo = mid; else hi = mid;
            }
            float u = (t - times[lo]) / Mathf.Max(times[hi] - times[lo], 1e-5f);
            var a = channel.Values[lo];
            var b = channel.Values[hi];
            if (!channel.IsRotation) return Vector4.Lerp(a, b, u);
            var q = Quaternion.Slerp(new Quaternion(a.x, a.y, a.z, a.w), new Quaternion(b.x, b.y, b.z, b.w), u);
            return new Vector4(q.x, q.y, q.z, q.w);
        }
    }
}
