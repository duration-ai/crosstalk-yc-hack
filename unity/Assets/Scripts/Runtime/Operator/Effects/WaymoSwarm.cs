using System;
using System.Collections.Generic;
using Dogpatch.Gameplay;
using Dogpatch.Race;
using Dogpatch.Smash;
using Dogpatch.Traffic;
using Dogpatch.Vehicles;
using UnityEngine;

namespace Dogpatch.Operator
{
    // waymo_swarm (a world effect): 6 to 8 Waymos spawn around each racer within 300 m of the caller,
    // chase and ram them until the row ends, then peel off and despawn. Every phone spawns the same
    // swarms from the row id and each target's player id, so the counts and spawn spots match; each
    // phone only lets the swarm hunting its own player touch it, and the swarms hunting other players
    // pass through this phone's player and car. Rubber-band speed keeps them on you but dodgeable,
    // and hits go through the normal damage system.
    public sealed class WaymoSwarmEffect : OperatorEffect
    {
        // Chris and Sudhir: the asker is spared. Set to true to chase everyone nearby instead.
        public static bool ChaseCaller = false;

        const float Reach = 300f;

        readonly List<SwarmBot> bots = new List<SwarmBot>();

        public override void Apply(string strength, EffectTarget target)
        {
            var game = Context.Game;
            var race = Context.Race;
            string me = race != null && !string.IsNullOrEmpty(race.MyId) ? race.MyId : EntityTagger.MyPlayerId;
            var players = new List<(string id, Func<Transform> body, bool mine)>
            {
                (me, () => game.CurrentCar != null ? game.CurrentCar.transform : game.Player.transform, true),
            };
            if (race != null && race.Remotes != null)
                foreach (var remote in race.Remotes.All)
                {
                    var r = remote;
                    players.Add((r.Id, () => r.Left || r.Car == null || r.Avatar == null ? null : r.Body, false));
                }

            Vector3? caller = null;
            foreach (var p in players)
                if (p.id == Row.requestedBy && p.body() != null) caller = p.body().position;

            int seed = Hash(Row.id);
            foreach (var (id, body, mine) in players)
            {
                var at = body();
                if (at == null || string.IsNullOrEmpty(id)) continue;
                if (!ChaseCaller && id == Row.requestedBy)
                {
                    if (mine) Debug.Log("[Swarm] you called it: no Waymos after you");
                    continue;
                }
                if (caller.HasValue && Vector3.Distance(caller.Value, at.position) > Reach) continue;
                Spawn(new System.Random(seed ^ Hash(id)), id, body, mine, at.position);
            }
        }

        void Spawn(System.Random rng, string id, Func<Transform> body, bool mine, Vector3 around)
        {
            int count = 6 + rng.Next(3);
            float baseAngle = (float)rng.NextDouble() * 360f;
            var graph = TrafficSystem.Instance != null ? TrafficSystem.Instance.Graph : null;
            var root = Context.Game.World.Root.transform;
            for (int i = 0; i < count; i++)
            {
                float angle = (baseAngle + i * 360f / count + (float)(rng.NextDouble() - 0.5) * 20f) * Mathf.Deg2Rad;
                float radius = 32f + (float)rng.NextDouble() * 14f;
                var spot = around + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                float heading = Mathf.Atan2(around.x - spot.x, around.z - spot.z) * Mathf.Rad2Deg;
                var lane = graph?.Nearest(new Vector2(spot.x, spot.z), Vector2.zero, 25f, out float s, out _);
                if (lane != null)
                {
                    var p = lane.Path.Sample(lane.Path.Closest(new Vector2(spot.x, spot.z), out _), out _);
                    spot = new Vector3(p.x, around.y, p.y);
                }
                spot.y = Mathf.Max(spot.y, 0f) + 0.1f;
                var car = VehicleRoster.Spawn(i % 3 == 2 ? VehicleTypes.WaymoOjai : VehicleTypes.WaymoSuv, spot, heading, root);
                car.name = "Swarm Waymo " + i + " for " + id;
                car.Enterable = false;
                car.Npc = true;
                if (car.GetComponent<VehicleDamage>() == null) car.gameObject.AddComponent<VehicleDamage>();
                var bot = car.gameObject.AddComponent<SwarmBot>();
                bot.Target = body;
                bot.Mine = mine;
                bot.Game = Context.Game;
                car.SetDriven(true);
                bots.Add(bot);
            }
            Debug.Log(mine ? $"[Swarm] {count} Waymos are after you" : $"[Swarm] {count} Waymos are after {id} (they pass through you)");
        }

        public override void Revert()
        {
            foreach (var bot in bots)
                if (bot != null) bot.Leave();
            bots.Clear();
        }

        static int Hash(string text)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in text ?? "") h = (h ^ c) * 16777619;
                return h;
            }
        }
    }

    // One swarm Waymo: steers at its target with rubber-band speed, its roof lidar strobing red; when
    // the row ends it drives off for a few seconds and despawns. A Waymo hunting another player
    // ignores this phone's player and car, so only the target's own phone resolves its hits.
    public sealed class SwarmBot : MonoBehaviour
    {
        public Func<Transform> Target;
        public bool Mine;
        public GameController Game;

        CarController car;
        Light strobe;
        float leaveUntil = -1f;
        Vector3 leaveDir;
        float nextIgnore;
        readonly HashSet<Collider> ignored = new HashSet<Collider>();

        void Start()
        {
            car = GetComponent<CarController>();
            var lidar = VehicleRoster.RigPart(car, "lidar");
            var light = new GameObject("swarm strobe");
            light.transform.SetParent(lidar != null ? lidar : transform, false);
            light.transform.localPosition = lidar != null ? Vector3.up * 0.2f : Vector3.up * 2.4f;
            strobe = light.AddComponent<Light>();
            strobe.type = LightType.Point;
            strobe.color = new Color(1f, 0.12f, 0.1f);
            strobe.range = 9f;
            if (lidar != null && lidar.GetComponent<Spinner>() is Spinner spin) spin.DegreesPerSecond = 1400f;
            if (Mine) VehicleImpacts.Hit += OnHit;
        }

        void OnDestroy()
        {
            if (Mine) VehicleImpacts.Hit -= OnHit;
        }

        void OnHit(VehicleHit hit)
        {
            if (hit.OtherCar == car && Game != null && hit.Car == Game.CurrentCar)
                Debug.Log($"[Swarm] a Waymo rammed you at {hit.ImpactSpeed:F1} m/s");
        }

        public void Leave()
        {
            if (leaveUntil > 0f) return;
            var t = Target?.Invoke();
            leaveDir = t != null ? transform.position - t.position : transform.forward;
            leaveDir.y = 0f;
            leaveDir = leaveDir.sqrMagnitude > 0.01f ? leaveDir.normalized : transform.forward;
            leaveUntil = Time.time + 3.5f;
        }

        void Update()
        {
            if (car == null) return;
            strobe.intensity = Mathf.Repeat(Time.time * 4f, 1f) < 0.5f ? 4f : 0f;
            if (leaveUntil > 0f)
            {
                car.SpeedMul = 1f;
                car.SetInput(leaveDir, 0.8f);
                if (Time.time > leaveUntil) Destroy(gameObject);
                return;
            }
            var target = Target?.Invoke();
            if (target == null)
            {
                Leave();
                return;
            }
            var to = target.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            car.SpeedMul = Mathf.Lerp(0.9f, 1.55f, Mathf.InverseLerp(12f, 60f, distance));
            car.SetInput(distance > 0.5f ? to / distance : transform.forward, 1f);
            if (!Mine && Time.time >= nextIgnore) IgnoreThisPhonesPlayer();
        }

        void IgnoreThisPhonesPlayer()
        {
            nextIgnore = Time.time + 0.5f;
            if (Game == null) return;
            var mine = new List<Collider>(Game.Player.GetComponentsInChildren<Collider>(true));
            if (Game.CurrentCar != null) mine.AddRange(Game.CurrentCar.GetComponentsInChildren<Collider>(true));
            var own = GetComponentsInChildren<Collider>(true);
            foreach (var other in mine)
            {
                if (other == null || !ignored.Add(other)) continue;
                foreach (var c in own) Physics.IgnoreCollision(c, other, true);
            }
        }
    }
}
