using System.Collections;
using System.Collections.Generic;
using Dogpatch.Operator;
using UnityEngine;

namespace Dogpatch.Traffic
{
    // Operator hooks (CROS-39). Rush hour and empty roads scale the ambient quotas: new cars pop in
    // around the player at once, surplus cars vanish in a puff, nearest first. Road rage makes
    // every AI driver faster, tailgate, run lights, honk and weave; Pace is one agent's own speed.
    public sealed partial class TrafficSystem
    {
        public static readonly Knob Density = new Knob();

        Coroutine refill;

        int Quota(int quota) => Mathf.RoundToInt(quota * Density.Product);

        public void Rebalance()
        {
            Thin();
            if (refill != null) StopCoroutine(refill);
            refill = StartCoroutine(Refill());
        }

        void Thin()
        {
            var focus = Focus;
            var groups = new[] { new List<TrafficAgent>(), new List<TrafficAgent>(), new List<TrafficAgent>() };
            foreach (var a in Agents)
            {
                if (a.OnRail || a.Route != null || a.Car == game.Current || a.GetComponent<PopOut>() != null) continue;
                groups[a.Spec.Robotaxi ? 0 : a.Spec.Rider ? 2 : 1].Add(a);
            }
            int[] quotas = { Quota(RobotaxiQuota), Quota(EverydayQuota), Quota(RiderQuota) };
            for (int g = 0; g < 3; g++)
            {
                var list = groups[g];
                list.Sort((x, y) => Score(x, focus).CompareTo(Score(y, focus)));
                for (int i = 0; i < list.Count - quotas[g]; i++)
                {
                    var agent = list[i];
                    PopOut.Start(agent.gameObject, () => Despawn(agent));
                }
            }
        }

        float Score(TrafficAgent a, Vector3 focus) => (a.transform.position - focus).magnitude + (Visible(a.transform.position) ? 0f : 200f);

        IEnumerator Refill()
        {
            nextCandidates = 0f;
            float until = Time.time + 6f;
            while (Time.time < until && Deficit(out _) > 0)
            {
                if (Time.time >= nextCandidates - 1.5f) nextCandidates = 0f;
                for (int k = 0; k < 16; k++)
                {
                    int before = Agents.Count;
                    TrySpawn(true);
                    if (Agents.Count > before) PopIn.Start(Agents[Agents.Count - 1].gameObject);
                }
                yield return null;
            }
            refill = null;
        }
    }

    public sealed partial class TrafficAgent
    {
        public static float Rage;
        public static bool AllStop;
        public float Pace = 1f;

        float Weave => Rage > 0f && !OnRail && Link == null && Lane != null ? Mathf.Sin(Time.time * 2.6f + GetInstanceID() * 0.37f) * Lane.Width * 0.2f * Rage : 0f;
    }
}
