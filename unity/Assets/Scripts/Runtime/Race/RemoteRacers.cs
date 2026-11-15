using System.Collections.Generic;
using System.Linq;
using Dogpatch.Gameplay;
using Dogpatch.WorldChanges;
using UnityEngine;

namespace Dogpatch.Race
{
    // Other players' racers, seen through the poll: a car in their slot color during the race, and
    // on foot in the lobby (or in a car when they move at driving speed). Positions arrive at the
    // poll rate stamped with the server time of each race/update, so each racer is drawn about
    // 150 ms behind the newest sample, interpolating between samples and extrapolating briefly when
    // one is late. Their cars are triggers, not solid: driving into one gives this device's car a
    // light push apart instead of an authoritative collision.
    public sealed class RemoteRacers : MonoBehaviour
    {
        public const float BufferMs = 150f;
        const float MaxExtrapolateMs = 250f;
        const float TeleportMeters = 40f;
        const float DrivingSpeed = 8f;
        const float PushEvery = 0.2f;

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
            public int Slot;
            public CarController Car;
            public RacerAvatar Avatar;
            public bool Driving;
            public bool Left;
            public float Speed;
            public Vector3 Position;
            public float Heading;
            internal readonly List<Sample> Samples = new List<Sample>();
            internal long LastUpdatedAt;
            internal float NextPush;

            public Transform Body => Left ? null : Driving ? Car.transform : Avatar.transform;
        }

        readonly Dictionary<string, Remote> remotes = new Dictionary<string, Remote>();
        double lagMs = 120.0;
        bool lagKnown;

        public IEnumerable<Remote> All => remotes.Values;

        public Remote Get(string playerId) => playerId != null && remotes.TryGetValue(playerId, out var r) ? r : null;

        // How far behind the server clock remote racers are drawn.
        public double DelayMs => lagMs + BufferMs;

        public void Sync(PollResponse poll)
        {
            var seen = new HashSet<string>();
            foreach (var racer in poll.racers)
            {
                if (!Race.IsRemote(racer.playerId)) continue;
                seen.Add(racer.playerId);
                var remote = Get(racer.playerId) ?? Spawn(racer);
                remote.Left = racer.left;
                remote.Name = racer.displayName;
                if (racer.updatedAt <= remote.LastUpdatedAt || racer.position == null) continue;
                remote.LastUpdatedAt = racer.updatedAt;
                double lag = poll.serverTime - racer.updatedAt;
                lagMs = lagKnown ? lagMs + (lag - lagMs) * 0.1 : lag;
                lagKnown = true;
                var p = new Vector3(racer.position.x, racer.position.y, racer.position.z);
                if (remote.Samples.Count > 0 && Vector3.Distance(remote.Samples[remote.Samples.Count - 1].P, p) > TeleportMeters) remote.Samples.Clear();
                remote.Samples.Add(new Sample { T = racer.updatedAt, P = p, H = racer.heading });
                if (remote.Samples.Count > 24) remote.Samples.RemoveAt(0);
            }
            foreach (var id in remotes.Keys.Where(id => !seen.Contains(id)).ToList()) Despawn(id);
        }

        public void Clear()
        {
            foreach (var id in remotes.Keys.ToList()) Despawn(id);
        }

        Remote Spawn(Racer racer)
        {
            var game = Race.Game;
            var color = PaintJob.Linear(string.IsNullOrEmpty(racer.colorHex) ? RaceDirector.SlotHex[Mathf.Clamp(racer.slot, 0, 3)] : racer.colorHex);
            var p = racer.position != null ? new Vector3(racer.position.x, racer.position.y, racer.position.z) : Race.Course.RingCenter;
            var carAsset = game.Models.Get(Race.MyCar.ModelId);
            var car = CarController.Create("remote-" + racer.playerId, carAsset, game.SharedMaterial, p, racer.heading);
            car.name = "Car " + racer.displayName;
            car.Enterable = false;
            car.transform.SetParent(game.World.Root.transform, true);
            foreach (var c in car.GetComponents<Collider>()) c.isTrigger = true;
            var body = car.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            CarFx.On(car);
            PaintJob.For(car.gameObject, car.ModelId).SetBase(color);
            Race.Changes.Paint(car);

            var avatar = RacerAvatar.Create(game.Models.Get(game.Models.Role("player")[0].id), game.SharedMaterial, color, racer.displayName);
            avatar.transform.SetParent(game.World.Root.transform, true);
            avatar.Stand(p, racer.heading);

            var remote = new Remote { Id = racer.playerId, Name = racer.displayName, Color = racer.color, Slot = racer.slot, Car = car, Avatar = avatar, Position = p, Heading = racer.heading };
            remotes[racer.playerId] = remote;
            Debug.Log($"[SpeedDial] remote racer {racer.displayName} ({racer.color}) joined");
            return remote;
        }

        void Despawn(string id)
        {
            var remote = remotes[id];
            remotes.Remove(id);
            if (remote.Car != null) Destroy(remote.Car.gameObject);
            if (remote.Avatar != null) Destroy(remote.Avatar.gameObject);
            Debug.Log($"[SpeedDial] remote racer {remote.Name} left");
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
            double t = Race.NowMs - DelayMs;
            var mine = Race.Game.CurrentCar;
            foreach (var remote in remotes.Values)
            {
                if (!Pose(remote.Samples, t, out var p, out float h)) continue;
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                float moved = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(remote.Position.x, 0f, remote.Position.z)) / dt;
                remote.Speed = Mathf.Lerp(remote.Speed, moved, 1f - Mathf.Exp(-4f * dt));
                remote.Position = p;
                remote.Heading = h;
                remote.Driving = Race.Gridded || remote.Speed > DrivingSpeed || (remote.Driving && remote.Speed > DrivingSpeed * 0.4f);

                remote.Car.gameObject.SetActive(!remote.Left && (remote.Driving || !Race.Gridded));
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
