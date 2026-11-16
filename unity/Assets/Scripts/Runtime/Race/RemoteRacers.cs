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
        const double MinBufferMs = 80.0;
        const double MaxBufferMs = 120.0;
        const float MaxExtrapolateMs = 250f;
        const float MaxDeadReckonMs = 1000f;
        const float MaxYawRate = 120f;
        const float SnapMeters = 5f;
        const float CorrectRate = 10f;
        const float TeleportMeters = 40f;
        const float DrivingSpeed = 8f;
        const float PushEvery = 0.2f;
        const float BumpShare = 0.6f;
        const float MinShove = 1.5f;
        const float MaxShove = 10f;
        const float BumpDedupe = 0.6f;
        const float BumpSnapHold = 0.3f;
        const float ForgetAfter = 15f;
        const float ModeFreshFor = 3f;

        public RaceDirector Race;

        public struct Sample
        {
            public double T;
            public Vector3 P;
            public float H;
            public Vector3 V;
            public float W;
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
            public int Protocol;
            internal string SwapType;
            internal GameObject Rider;
            internal bool LookKnown;
            internal int Paint = -1;
            internal string Tint;
            internal string AvatarId;
            internal float WorldSeenAt = -1000f;
            internal long ModeSince;
            internal long WorldUpdatedAt;
            internal double IntervalMs = MaxBufferMs;
            internal bool IntervalKnown;

            // Just over the time between this player's updates, so their newest sample is usually in
            // hand: about 80 ms for a phone sending 15/s, at most 120 ms, with dead reckoning
            // covering slower senders.
            public double BufferMs => System.Math.Min(MaxBufferMs, System.Math.Max(MinBufferMs, IntervalMs * 1.25));
            public string RaceColorHex;
            public string WorldColor;
            public string WorldColorHex;
            internal readonly List<Sample> Samples = new List<Sample>();
            internal long LastUpdatedAt;
            internal float NextPush;
            internal float ContactAt = -10f;
            internal float BumpedAt = -10f;
            internal long LastBumpAt;
            internal float SnapHoldUntil;
            internal float SeenAt;
            internal bool Placed;
            internal Vector3 Velocity;
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

        // The average of every remote's own buffer, for the logs.
        public double BufferMs => remotes.Count == 0 ? MaxBufferMs : remotes.Values.Average(r => r.BufferMs);

        // Race room: every racer this device does not drive.
        public void Sync(PollResponse poll)
        {
            var racing = new HashSet<string>();
            foreach (var racer in poll.racers)
            {
                if (!Race.IsRemote(racer.playerId)) continue;
                if (racer.left)
                {
                    var gone = Get(racer.playerId);
                    if (gone != null) gone.Left = Time.time - gone.WorldSeenAt > 2f;
                    continue;
                }
                racing.Add(racer.playerId);
                var remote = Get(racer.playerId) ?? Spawn(racer.playerId, racer.displayName, racer.position);
                remote.InRace = true;
                remote.Left = false;
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
                try { SyncWorld(player, world.serverTime); }
                catch (System.Exception e)
                {
                    if (broken.Add(player.playerId)) Debug.LogWarning($"[SpeedDial] remote player {player.displayName} could not be updated: {e}");
                }
            }
        }

        // Players whose update threw, each logged once so one bad row neither spams nor stops the rest.
        readonly HashSet<string> broken = new HashSet<string>();

        void SyncWorld(WorldPlayer player, long serverTime)
        {
            {
                var remote = Get(player.playerId) ?? Spawn(player.playerId, player.displayName, player.position);
                remote.Name = player.displayName;
                remote.WorldColor = player.color;
                remote.WorldColorHex = player.colorHex;
                if (player.mode != remote.WorldMode) remote.ModeSince = player.updatedAt;
                remote.WorldMode = player.mode;
                remote.Protocol = player.protocol;
                var bump = player.bump;
                if (bump != null && bump.target == Race.MyId && bump.at > remote.LastBumpAt)
                {
                    remote.LastBumpAt = bump.at;
                    if (serverTime - bump.at < 1500) Bumped(remote, new Vector3(bump.x, 0f, bump.z), serverTime - bump.at);
                }
                remote.SeenAt = Time.time;
                if (player.updatedAt > remote.WorldUpdatedAt)
                {
                    remote.WorldUpdatedAt = player.updatedAt;
                    remote.WorldSeenAt = Time.time;
                    remote.Left = false;
                }
                var look = player.look;
                if (look != null && look.Known)
                {
                    if (!remote.LookKnown || look.tint != remote.Tint) remote.PaintedHex = null;
                    remote.LookKnown = true;
                    remote.Tint = look.tint ?? "";
                    remote.Paint = look.paint;
                    if (!string.IsNullOrEmpty(look.avatar) && look.avatar != remote.AvatarId)
                    {
                        remote.AvatarId = look.avatar;
                        Wear(remote, look.avatar);
                    }
                }
                var want = player.vehicleType + "#" + remote.Paint;
                if (want != remote.SwapType)
                {
                    remote.SwapType = want;
                    var identity = remote.Car.GetComponent<Vehicles.VehicleIdentity>();
                    bool repaint = remote.Paint >= 0 && identity != null && identity.Paint != remote.Paint;
                    if (Vehicles.VehicleCatalog.Has(player.vehicleType) && (repaint || player.vehicleType != Vehicles.VehicleRoster.TypeOf(remote.Car)))
                    {
                        Vehicles.VehicleRoster.SwapInto(remote.Car, player.vehicleType, remote.Paint);
                        SeatRider(remote);
                        foreach (var c in remote.Car.GetComponents<Collider>()) c.isTrigger = true;
                        remote.PaintedHex = null;
                    }
                }
                if (!remote.InRace) AddSample(remote, player.updatedAt, player.position, player.heading, serverTime);
            }
        }

        // The figure the player made from their photo, fetched by the id their presence names and
        // swapped in for the stock one, untinted, as it looks on their phone.
        async void Wear(Remote remote, string avatarId)
        {
            try
            {
                var avatar = await new Dogpatch.Backend.AvatarHttpClient(Dogpatch.Backend.AvatarHttpClient.ConfiguredBaseUrl()).GetAvatarAsync(avatarId);
                if (avatar == null || avatar.IsEmpty || remote.AvatarId != avatarId || Get(remote.Id) != remote) return;
                var game = Race.Game;
                var glb = await Dogpatch.Glb.AvatarLoader.LoadAsync(avatar, game.Models.Get(game.Models.Role("player")[0].id));
                if (glb == null || remote.AvatarId != avatarId || Get(remote.Id) != remote) return;
                var old = remote.Avatar;
                var fresh = RacerAvatar.Create(glb, game.SharedMaterial, Color.white, remote.Name);
                PaintJob.For(fresh.gameObject, glb.Id).SetBase(null);
                fresh.transform.SetParent(old.transform.parent, true);
                fresh.Stand(old.transform.position, old.transform.eulerAngles.y);
                fresh.gameObject.SetActive(old.gameObject.activeSelf);
                remote.Avatar = fresh;
                Destroy(old.gameObject);
                Debug.Log($"[SpeedDial] remote player {remote.Name} wears their avatar ({avatar.@base})");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpeedDial] {remote.Name}'s avatar did not load: {e.Message}");
            }
        }

        // On a bike, scooter or cart the player's own figure rides it, as it does on their phone,
        // in place of the courier the roster seats on delivery bikes.
        static void SeatRider(Remote remote)
        {
            if (remote.Rider != null) Destroy(remote.Rider);
            remote.Rider = Vehicles.VehicleRoster.MountPlayer(remote.Car);
            if (remote.Rider == null) return;
            var identity = remote.Car.GetComponent<Vehicles.VehicleIdentity>();
            if (identity != null && identity.Rider != null)
            {
                Destroy(identity.Rider);
                identity.Rider = null;
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
            var sample = new Sample { T = updatedAt, P = p, H = heading };
            if (remote.Samples.Count > 0)
            {
                var prev = remote.Samples[remote.Samples.Count - 1];
                float span = (float)(updatedAt - prev.T) / 1000f;
                if (span > 0.01f && span < 1f)
                {
                    remote.IntervalMs = remote.IntervalKnown ? remote.IntervalMs + (span * 1000.0 - remote.IntervalMs) * 0.1 : span * 1000.0;
                    remote.IntervalKnown = true;
                }
                if (span > 0.02f)
                {
                    sample.V = Vector3.Lerp(prev.V, (p - prev.P) / span, 0.6f);
                    sample.W = Mathf.Lerp(prev.W, Mathf.Clamp(Mathf.DeltaAngle(prev.H, heading) / span, -MaxYawRate, MaxYawRate), 0.6f);
                }
                else
                {
                    sample.V = prev.V;
                    sample.W = prev.W;
                }
            }
            remote.Samples.Add(sample);
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
            var carAsset = game.Models.Entry(Race.MyCar.ModelId) != null ? game.Models.Get(Race.MyCar.ModelId) : game.Models.Get(game.Models.Role("car")[0].id);
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
            Debug.Log($"[SpeedDial] remote player {remote.Name} left after {Time.time - remote.SeenAt:F0} s unseen");
        }

        // Slot colour while racing together, world colour otherwise.
        static void Paint(Remote remote, RaceDirector race)
        {
            var hex = remote.InRace && !string.IsNullOrEmpty(remote.RaceColorHex) ? remote.RaceColorHex : remote.LookKnown ? remote.Tint : remote.WorldColorHex;
            if (hex == null || (hex == "" && !remote.LookKnown) || hex == remote.PaintedHex) return;
            remote.PaintedHex = hex;
            if (!remote.InRace) remote.Color = remote.WorldColor;
            var color = hex == "" ? (Color?)null : PaintJob.Linear(hex);
            PaintJob.For(remote.Car.gameObject, remote.Car.ModelId).SetBase(color);
            race.Changes.Paint(remote.Car);
            if (remote.AvatarId == null) PaintJob.For(remote.Avatar.gameObject, race.Game.Models.Role("player")[0].id).SetBase(remote.LookKnown ? null : color);
        }

        // Pose at server time t: interpolated between samples, and past the newest one dead-reckoned
        // from its velocity and turn rate for up to a second.
        public static bool Predict(List<Sample> samples, double t, out Vector3 p, out float h, out Vector3 v)
        {
            v = Vector3.zero;
            if (samples.Count == 0)
            {
                p = default;
                h = 0f;
                return false;
            }
            var last = samples[samples.Count - 1];
            if (t <= last.T || samples.Count < 2)
            {
                for (int i = samples.Count - 1; i > 0; i--)
                {
                    var a = samples[i - 1];
                    var b = samples[i];
                    if (t < a.T) continue;
                    if (b.T > a.T) v = (b.P - a.P) / (float)((b.T - a.T) / 1000.0);
                    break;
                }
                return Pose(samples, t, out p, out h);
            }
            float ahead = (float)System.Math.Min(t - last.T, MaxDeadReckonMs) / 1000f;
            v = t - last.T >= MaxDeadReckonMs ? Vector3.zero : Quaternion.Euler(0f, last.W * ahead, 0f) * last.V;
            p = last.P + Quaternion.Euler(0f, last.W * ahead * 0.5f, 0f) * last.V * ahead;
            h = last.H + last.W * ahead;
            return true;
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

        readonly List<string> forgotten = new List<string>();

        void Update()
        {
            if (Race == null || remotes.Count == 0) return;
            if (!Race.Reconnecting)
            {
                forgotten.Clear();
                foreach (var r in remotes.Values)
                    if (Time.time - r.SeenAt > ForgetAfter) forgotten.Add(r.Id);
                foreach (var gone in forgotten) Despawn(gone);
            }
            double now = Race.NowMs - lagMs;
            var mine = Race.Game.CurrentCar;
            foreach (var remote in remotes.Values)
            {
                Paint(remote, Race);
                if (!Predict(remote.Samples, now - remote.BufferMs, out var target, out float targetH, out var velocity)) continue;
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                remote.Velocity = velocity;
                float snapAt = Time.time < remote.SnapHoldUntil ? SnapMeters * 4f : SnapMeters;
                if (!remote.Placed || Vector3.Distance(remote.Position, target) > snapAt)
                {
                    remote.Placed = true;
                    remote.Position = target;
                    remote.Heading = targetH;
                }
                else
                {
                    float k = 1f - Mathf.Exp(-CorrectRate * dt);
                    remote.Position = Vector3.Lerp(remote.Position + velocity * dt, target, k);
                    remote.Heading = Mathf.LerpAngle(remote.Heading, targetH, k);
                }
                var p = remote.Position;
                float h = remote.Heading;
                remote.Speed = Mathf.Lerp(remote.Speed, new Vector2(velocity.x, velocity.z).magnitude, 1f - Mathf.Exp(-6f * dt));
                bool modeKnown = !string.IsNullOrEmpty(remote.WorldMode) && Time.time - remote.WorldSeenAt < ModeFreshFor;
                bool wasDriving = remote.Driving;
                remote.Driving = modeKnown
                    ? remote.WorldMode == Dogpatch.Backend.PlayerModes.Driving
                    : (remote.InRace && Race.Gridded) || remote.Speed > DrivingSpeed || (remote.Driving && remote.Speed > DrivingSpeed * 0.4f);
                if (remote.Driving != wasDriving) Debug.Log($"[SpeedDial] remote player {remote.Name} is {(remote.Driving ? "driving" : "on foot")}, {Race.NowMs - remote.ModeSince:F0} ms after they switched");

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

        // Overlapping boxes shove this device's car away from the remote car, harder the faster the two
        // were closing, like hitting a heavy traffic car; the first contact of a hit also sends the
        // remote phone the matching shove. A hit their phone already sent us is not felt twice.
        void PushApart(Remote remote, CarController mine)
        {
            var other = remote.Car.transform;
            var rel = other.InverseTransformPoint(mine.transform.position);
            var reach = remote.Car.HalfExtents + mine.HalfExtents;
            if (Mathf.Abs(rel.x) > reach.x || Mathf.Abs(rel.z) > reach.z || Mathf.Abs(rel.y) > 3f) return;
            if (Time.time - remote.BumpedAt < BumpDedupe) return;
            var away = mine.transform.position - other.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = other.right;
            away.Normalize();
            var before = Planar(mine);
            bool first = Time.time - remote.ContactAt > BumpDedupe;
            float closing = Vector3.Dot(new Vector3(remote.Velocity.x, 0f, remote.Velocity.z) - before, away);
            closing = first ? Mathf.Abs(closing) : Mathf.Max(0f, closing);
            float shove = Mathf.Clamp(closing * BumpShare, MinShove, MaxShove);
            Shove(mine, away * shove);
            remote.NextPush = Time.time + PushEvery;
            remote.SnapHoldUntil = Time.time + BumpSnapHold;
            remote.ContactAt = Time.time;
            if (first) Race.SendBump(remote.Id, -away * shove);
            Debug.Log($"[SpeedDial] bumped {remote.Name}'s car: closing {closing:F1} m/s, shove {shove:F1} m/s{(first ? ", sent them the bump" : "")}");
            if (first) StartCoroutine(LogDeltaV(mine, before, "after hitting " + remote.Name));
        }

        // A hit the remote phone felt with this device's car: the matching shove, unless this device
        // already felt the same contact itself.
        void Bumped(Remote remote, Vector3 impulse, long ageMs)
        {
            remote.SnapHoldUntil = Time.time + BumpSnapHold;
            var mine = Race.Game.CurrentCar;
            if (mine == null) return;
            if (Time.time - remote.ContactAt < BumpDedupe)
            {
                Debug.Log($"[SpeedDial] {remote.Name}'s bump ({ageMs} ms old) already felt here");
                return;
            }
            remote.BumpedAt = Time.time;
            impulse = Vector3.ClampMagnitude(impulse, MaxShove);
            var before = Planar(mine);
            Shove(mine, impulse);
            Debug.Log($"[SpeedDial] {remote.Name} bumped us: shove {impulse.magnitude:F1} m/s, {ageMs} ms after their hit");
            StartCoroutine(LogDeltaV(mine, before, "after " + remote.Name + "'s bump"));
        }

        // CarFx's push is a decaying velocity offset, so its total velocity change grows with its
        // square (about 1x at 1 m/s); this asks it for the velocity change itself.
        static void Shove(CarController car, Vector3 dv) => CarFx.On(car).Push(dv.normalized * Mathf.Sqrt(dv.magnitude));

        static Vector3 Planar(CarController car)
        {
            var v = car.GetComponent<Rigidbody>().linearVelocity;
            return new Vector3(v.x, 0f, v.z);
        }

        System.Collections.IEnumerator LogDeltaV(CarController car, Vector3 before, string what)
        {
            yield return new WaitForSeconds(0.25f);
            if (car == null) yield break;
            var dv = Planar(car) - before;
            Debug.Log($"[SpeedDial] velocity change {what}: {dv.magnitude:F1} m/s ({dv.x:F1},{dv.z:F1})");
        }
    }
}
