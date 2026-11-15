using System.Collections.Generic;
using System.Linq;
using Dogpatch.Gameplay;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // Other players, from two sources: the shared world's presence (everyone in this shard, on foot
    // or in their vehicle, in their world colour) and the race room's poll (racers in their slot
    // colour, in their car from the grid on). One Remote per player takes samples from both, stamped
    // with the server time of each update, and is drawn about 150 ms behind the newest sample,
    // interpolating between samples and extrapolating briefly when one is late. Their cars are
    // triggers, not solid: driving into one gives this device's car a light push apart.
    public sealed class RemoteRacers : MonoBehaviour
    {
        public const float BufferMs = 150f;
        const float MaxExtrapolateMs = 250f;
        const float TeleportMeters = 40f;
        const float DrivingSpeed = 8f;
        const float PushEvery = 0.2f;
        const float ForgetAfter = 12f;

        public RaceDirector Race;

        public struct Sample
        {
            public double T;
            public Vector3 P;
            public float H;
        }

        public sealed class Remote
        {
            public string Id;
            public string Name;
            public string Color;
            public string ColorHex;
            public CarController Car;
            public RacerAvatar Avatar;
            public bool Driving;
            public bool Left;
            public bool InRace;
            public float Speed;
            public Vector3 Position;
            public float Heading;
            public string WorldMode;
            public string RaceColorHex;
            public string WorldColor;
            public string WorldColorHex;
            internal readonly List<Sample> Samples = new List<Sample>();
            internal long LastUpdatedAt;
            internal float NextPush;
            internal float SeenAt;
            internal string PaintedHex;

            public Transform Body => Left ? null : Driving ? Car.transform : Avatar.transform;
        }

        readonly Dictionary<string, Remote> remotes = new Dictionary<string, Remote>();
        double lagMs = 120.0;
        bool lagKnown;

        public IEnumerable<Remote> All => remotes.Values;

        public Remote Get(string playerId) => playerId != null && remotes.TryGetValue(playerId, out var r) ? r : null;

        // How far behind the server clock remote players are drawn.
        public double DelayMs => lagMs + BufferMs;

        // Race room: every racer this device does not drive.
        public void Sync(PollResponse poll)
        {
            var racing = new HashSet<string>();
            foreach (var racer in poll.racers)
            {
                if (!Race.IsRemote(racer.playerId)) continue;
                racing.Add(racer.playerId);
                var remote = Get(racer.playerId) ?? Spawn(racer.playerId, racer.displayName, racer.position);
                remote.InRace = true;
                remote.Left = racer.left;
                remote.Name = racer.displayName;
                remote.RaceColorHex = racer.colorHex;
                remote.Color = racer.color;
                remote.SeenAt = Time.time;
                AddSample(remote, racer.updatedAt, racer.position, racer.heading, poll.serverTime);
            }
            foreach (var remote in remotes.Values)
                if (!racing.Contains(remote.Id)) remote.InRace = false;
        }

        // Shared world: everyone else in this shard.
        public void SyncWorld(WorldPresenceResponse world)
        {
            foreach (var player in world.players)
            {
                if (!Race.IsRemote(player.playerId)) continue;
                var remote = Get(player.playerId) ?? Spawn(player.playerId, player.displayName, player.position);
                remote.Name = player.displayName;
                remote.WorldColor = player.color;
                remote.WorldColorHex = player.colorHex;
                remote.WorldMode = player.mode;
                remote.SeenAt = Time.time;
                if (Vehicles.VehicleCatalog.Has(player.vehicleType) && player.vehicleType != Vehicles.VehicleRoster.TypeOf(remote.Car))
                {
                    Vehicles.VehicleRoster.SwapInto(remote.Car, player.vehicleType);
                    foreach (var c in remote.Car.GetComponents<Collider>()) c.isTrigger = true;
                    remote.PaintedHex = null;
                }
                if (!remote.InRace) remote.Left = false;
                AddSample(remote, player.updatedAt, player.position, player.heading, world.serverTime);
            }
        }

        void AddSample(Remote remote, long updatedAt, Dogpatch.Backend.Vec3 position, float heading, long serverTime)
        {
            if (updatedAt <= remote.LastUpdatedAt || position == null) return;
            remote.LastUpdatedAt = updatedAt;
            double lag = serverTime - updatedAt;
            lagMs = lagKnown ? lagMs + (lag - lagMs) * 0.1 : lag;
            lagKnown = true;
            var p = new Vector3(position.x, position.y, position.z);
            if (remote.Samples.Count > 0 && Vector3.Distance(remote.Samples[remote.Samples.Count - 1].P, p) > TeleportMeters) remote.Samples.Clear();
            remote.Samples.Add(new Sample { T = updatedAt, P = p, H = heading });
            if (remote.Samples.Count > 24) remote.Samples.RemoveAt(0);
        }

        // Back in free roam: everyone goes back to their world colour.
        public void EndRace()
        {
            foreach (var remote in remotes.Values)
            {
                remote.InRace = false;
                remote.Left = false;
            }
        }

        public void Clear()
        {
            foreach (var id in remotes.Keys.ToList()) Despawn(id);
        }

        Remote Spawn(string playerId, string displayName, Dogpatch.Backend.Vec3 at)
        {
            var game = Race.Game;
            var p = at != null ? new Vector3(at.x, at.y, at.z) : Race.Course.RingCenter;
            var carAsset = game.Models.Get(Race.MyCar.ModelId);
            var car = CarController.Create("remote-" + playerId, carAsset, game.SharedMaterial, p, 0f);
            car.name = "Car " + displayName;
            car.Enterable = false;
            car.transform.SetParent(game.World.Root.transform, true);
            foreach (var c in car.GetComponents<Collider>()) c.isTrigger = true;
            car.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
            CarFx.On(car);

            var avatar = RacerAvatar.Create(game.Models.Get(game.Models.Role("player")[0].id), game.SharedMaterial, Color.white, displayName);
            avatar.transform.SetParent(game.World.Root.transform, true);
            avatar.Stand(p, 0f);

            var remote = new Remote { Id = playerId, Name = displayName, Car = car, Avatar = avatar, Position = p, SeenAt = Time.time };
            remotes[playerId] = remote;
            Debug.Log($"[SpeedDial] remote player {displayName} appeared");
            return remote;
        }

        void Despawn(string id)
        {
            var remote = remotes[id];
            remotes.Remove(id);
            if (remote.Car != null) Destroy(remote.Car.gameObject);
            if (remote.Avatar != null) Destroy(remote.Avatar.gameObject);
            Debug.Log($"[SpeedDial] remote player {remote.Name} left");
        }

        // Slot colour while racing together, world colour otherwise.
        static void Paint(Remote remote, RaceDirector race)
        {
            var hex = remote.InRace && !string.IsNullOrEmpty(remote.RaceColorHex) ? remote.RaceColorHex : remote.WorldColorHex;
            if (string.IsNullOrEmpty(hex) || hex == remote.PaintedHex) return;
            remote.PaintedHex = hex;
            if (!remote.InRace) remote.Color = remote.WorldColor;
            var color = PaintJob.Linear(hex);
            PaintJob.For(remote.Car.gameObject, remote.Car.ModelId).SetBase(color);
            race.Changes.Paint(remote.Car);
            PaintJob.For(remote.Avatar.gameObject, race.Game.Models.Role("player")[0].id).SetBase(color);
        }

        // Interpolated pose at server time t, from the samples around it.
        public static bool Pose(List<Sample> samples, double t, out Vector3 p, out float h)
        {
            p = default;
            h = 0f;
            if (samples.Count == 0) return false;
            var last = samples[samples.Count - 1];
            if (t >= last.T)
            {
                p = last.P;
                h = last.H;
                if (samples.Count < 2) return true;
                var prev = samples[samples.Count - 2];
                double span = last.T - prev.T;
                if (span <= 0) return true;
                double ahead = System.Math.Min(t - last.T, MaxExtrapolateMs);
                p = last.P + (last.P - prev.P) * (float)(ahead / span);
                return true;
            }
            if (t <= samples[0].T)
            {
                p = samples[0].P;
                h = samples[0].H;
                return true;
            }
            for (int i = samples.Count - 1; i > 0; i--)
            {
                var a = samples[i - 1];
                var b = samples[i];
                if (t < a.T) continue;
                float k = (float)((t - a.T) / System.Math.Max(1.0, b.T - a.T));
                p = Vector3.Lerp(a.P, b.P, k);
                h = Mathf.LerpAngle(a.H, b.H, k);
                return true;
            }
            return true;
        }

        void Update()
        {
            if (Race == null || remotes.Count == 0) return;
            if (!Race.Reconnecting)
                foreach (var gone in remotes.Values.Where(r => Time.time - r.SeenAt > ForgetAfter).Select(r => r.Id).ToList()) Despawn(gone);
            double t = Race.NowMs - DelayMs;
            var mine = Race.Game.CurrentCar;
            foreach (var remote in remotes.Values)
            {
                Paint(remote, Race);
                if (!Pose(remote.Samples, t, out var p, out float h)) continue;
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                float moved = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(remote.Position.x, 0f, remote.Position.z)) / dt;
                remote.Speed = Mathf.Lerp(remote.Speed, moved, 1f - Mathf.Exp(-4f * dt));
                remote.Position = p;
                remote.Heading = h;
                bool raceCar = remote.InRace && Race.Gridded;
                remote.Driving = raceCar || (remote.WorldMode != null
                    ? remote.WorldMode == Dogpatch.Backend.PlayerModes.Driving
                    : remote.Speed > DrivingSpeed || (remote.Driving && remote.Speed > DrivingSpeed * 0.4f));

                remote.Car.gameObject.SetActive(!remote.Left && remote.Driving);
                remote.Avatar.gameObject.SetActive(!remote.Left && !remote.Driving);
                if (remote.Driving)
                {
                    remote.Car.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, h, 0f));
                    remote.Car.SetAutopilotMotion(remote.Speed, 0f);
                }
                else remote.Avatar.Track(p, h, remote.Speed);

                if (mine != null && remote.Driving && !remote.Left && Time.time >= remote.NextPush) PushApart(remote, mine);
            }
        }

        // Overlapping boxes push this device's car out sideways, away from the remote car.
        void PushApart(Remote remote, CarController mine)
        {
            var other = remote.Car.transform;
            var rel = other.InverseTransformPoint(mine.transform.position);
            var reach = remote.Car.HalfExtents + mine.HalfExtents;
            if (Mathf.Abs(rel.x) > reach.x || Mathf.Abs(rel.z) > reach.z || Mathf.Abs(rel.y) > 3f) return;
            var away = mine.transform.position - other.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = other.right;
            CarFx.On(mine).Push(away.normalized * 3.5f);
            remote.NextPush = Time.time + PushEvery;
            Debug.Log($"[SpeedDial] bumped {remote.Name}'s car");
        }
    }
}
