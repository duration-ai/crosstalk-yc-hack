using System;
using System.Collections;
using System.Linq;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Operator
{
    // Two-client check of waymo_swarm, inert unless launched with -swarmTest caller|target. Both
    // clients wait until they see each other in the shared world, then apply the same row locally
    // (id "swarm-smoke", asked by the caller) as the activeEffects subscription would, and log how
    // many swarm Waymos exist, which of them hunt this player, and how close they get.
    public sealed class SwarmCapture : MonoBehaviour
    {
        string role;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-swarmTest");
            if (i < 0 || i + 1 >= args.Length) return;
            new GameObject("SwarmCapture").AddComponent<SwarmCapture>().role = args[i + 1];
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            float t0 = Time.realtimeSinceStartup;
            RaceDirector race = null;
            while (Time.realtimeSinceStartup - t0 < 150f)
            {
                race = RaceDirector.Instance;
                if (race != null && race.Setup && !string.IsNullOrEmpty(race.MyId) && race.Remotes != null && race.Remotes.All.Any(r => !r.Left) && OperatorRig.Instance?.Effects != null) break;
                yield return new WaitForSeconds(1f);
            }
            var idFile = Environment.GetEnvironmentVariable("SWARM_CALLER_FILE");
            if (role == "caller" && race != null && idFile != null) System.IO.File.WriteAllText(idFile, race.MyId);
            string callerId = null;
            for (int i = 0; role != "caller" && idFile != null && i < 60 && callerId == null; i++)
            {
                if (System.IO.File.Exists(idFile)) callerId = System.IO.File.ReadAllText(idFile).Trim();
                else yield return new WaitForSeconds(1f);
            }
            var other = role == "caller" ? race?.Remotes?.All.FirstOrDefault(r => !r.Left) : race?.Remotes?.All.FirstOrDefault(r => r.Id == callerId);
            for (int i = 0; other == null && callerId != null && i < 30; i++)
            {
                yield return new WaitForSeconds(1f);
                other = race.Remotes.All.FirstOrDefault(r => r.Id == callerId);
            }
            if (other == null)
            {
                Debug.Log("[SwarmTest] never saw the other client");
                Application.Quit();
                yield break;
            }
            yield return new WaitForSeconds(role == "caller" ? 4f : 3f);
            string caller = role == "caller" ? race.MyId : other.Id;
            var game = race.Game;
            Debug.Log($"[SwarmTest] {role}: I am {race.MyId}, the other is {other.Id} ({other.Name}) {Vector3.Distance(game.Player.transform.position, other.Position):F0} m away; caller {caller}");
            var effects = OperatorRig.Instance.Effects;
            double now = effects.ServerNow();
            effects.Optimistic(new ActiveEffect
            {
                id = "swarm-smoke", effect = "waymo_swarm", scope = "world", strength = "medium", status = "active",
                startsAt = now, endsAt = now + 20000, durationS = 20, label = "Waymo attack", requestedBy = caller,
            });
            float nearest = float.MaxValue;
            for (int tick = 0; tick < 13; tick++)
            {
                yield return new WaitForSeconds(2f);
                var bots = FindObjectsByType<SwarmBot>(FindObjectsSortMode.None);
                var me = game.CurrentCar != null ? game.CurrentCar.transform : game.Player.transform;
                int mine = bots.Count(b => b.Mine);
                float closest = bots.Where(b => b.Mine).Select(b => Vector3.Distance(b.transform.position, me.position)).DefaultIfEmpty(float.MaxValue).Min();
                nearest = Mathf.Min(nearest, closest);
                Debug.Log($"[SwarmTest] {role} t={tick * 2 + 2}s: {bots.Length} swarm Waymos, {mine} hunting me, closest {(closest < float.MaxValue ? closest.ToString("F1") + " m" : "none")}");
            }
            effects.ResetAll();
            yield return new WaitForSeconds(5f);
            Debug.Log($"[SwarmTest] {role} done: {FindObjectsByType<SwarmBot>(FindObjectsSortMode.None).Length} swarm Waymos left 5 s after the row ended; closest any got to me {(nearest < float.MaxValue ? nearest.ToString("F1") + " m" : "none came")}");
            Application.Quit();
        }
    }
}
