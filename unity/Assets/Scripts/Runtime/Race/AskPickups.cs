using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dogpatch.Audio;
using Dogpatch.Backend;
using Dogpatch.Map;
using UnityEngine;

namespace Dogpatch.Race
{
    // The spinning phones placed on the racing line at race setup (RaceCourse.PlaceAskPickups, tuned in
    // asks.json). Driving or walking through one while racing collects it through
    // IRaceSession.CollectAskAsync and opens the mic at once for one hands-free Operator call. Each racer can take each token once a
    // race, and a token you have taken disappears for you until the rematch. Other systems, like the
    // minimap, read Positions and Available and listen to Changed; each token the local player can
    // still take is tracked on the minimap through MapMarkers.
    public sealed class AskPickups : MonoBehaviour
    {
        public static AskPickups Instance { get; private set; }

        public static IReadOnlyList<Vector3> Positions => Instance != null ? Instance.positions : (IReadOnlyList<Vector3>)Array.Empty<Vector3>();

        // True while the local player can still collect pickup i this race.
        public static bool Available(int i) => Instance != null && (Instance.mask & (1UL << i)) != 0;

        // Raised when the set of pickups available to the local player changes.
        public static event Action Changed;

        public RaceDirector Race;
        public float Radius = 4f;

        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<GameObject> tokens = new List<GameObject>();
        readonly HashSet<(string, int)> asked = new HashSet<(string, int)>();
        int raceNumber;
        string roomId;
        ulong mask = ulong.MaxValue;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Place(IReadOnlyList<Vector3> spots, float radius, Material material, Transform parent)
        {
            Radius = radius;
            foreach (var spot in spots)
            {
                positions.Add(spot);
                var token = CourseDressing.AskToken(parent, spot, material);
                tokens.Add(token);
                MapMarkers.Track(token.transform, MapMarkerKind.Ask);
            }
        }

        ulong MyMask()
        {
            var me = Race.Me;
            if (me == null || Race.Room.status == RoomStatus.Finished) return 0UL;
            int served = Race.Room.pickups?.Count ?? 0;
            ulong m = 0UL;
            for (int i = 0; i < Mathf.Min(positions.Count, served); i++)
                if (me.collected == null || !me.collected.Contains(i)) m |= 1UL << i;
            return m;
        }

        void Update()
        {
            if (Race == null || !Race.Setup || Race.Last == null) return;
            var room = Race.Room;
            if (room.raceNumber != raceNumber || Race.RoomId != roomId)
            {
                raceNumber = room.raceNumber;
                roomId = Race.RoomId;
                asked.Clear();
            }

            var now = MyMask();
            if (now != mask)
            {
                mask = now;
                for (int i = 0; i < tokens.Count; i++)
                {
                    bool available = (mask & (1UL << i)) != 0;
                    tokens[i].SetActive(available);
                    if (available) MapMarkers.Track(tokens[i].transform, MapMarkerKind.Ask);
                    else MapMarkers.Untrack(tokens[i].transform);
                }
                Changed?.Invoke();
            }
            float spin = Time.time * 120f;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].activeSelf) continue;
                var coin = tokens[i].transform.GetChild(0);
                coin.SetPositionAndRotation(positions[i] + Vector3.up * (1.6f + Mathf.Sin(Time.time * 2.4f + i) * 0.15f), Quaternion.Euler(0f, spin + i * 40f, 0f));
            }

            if (room.status != RoomStatus.Racing) return;
            foreach (var racer in Race.Last.racers)
            {
                if (racer.left || racer.finished) continue;
                var body = Race.BodyOf(racer.playerId);
                if (body == null) continue;
                var p = body.position;
                for (int i = 0; i < Mathf.Min(positions.Count, room.pickups?.Count ?? 0); i++)
                {
                    if ((racer.collected != null && racer.collected.Contains(i)) || asked.Contains((racer.playerId, i))) continue;
                    var q = positions[i];
                    if (Mathf.Abs(p.y - q.y) > 3f || Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z)) > Radius) continue;
                    asked.Add((racer.playerId, i));
                    _ = CollectAsync(racer.playerId, i, p);
                }
            }
        }

        async Task CollectAsync(string playerId, int pickup, Vector3 at)
        {
            try
            {
                var result = await Race.Session.CollectAskAsync(playerId, Race.RoomId, pickup, new Vec3(at.x, at.y, at.z));
                if (!result.collected) return;
                Debug.Log($"[SpeedDial] {Race.NameOf(playerId)} collected ask pickup {pickup}: {result.asks} asks");
                if (playerId != Race.MyId) return;
                GameEvents.RaiseAskCollected(positions[pickup]);
                Race.Calls.Pickup();
            }
            catch (BackendException e)
            {
                asked.Remove((playerId, pickup));
                Debug.LogWarning($"[SpeedDial] pickup {pickup} refused for {Race.NameOf(playerId)}: {e.Code} {e.Message}");
            }
        }
    }
}
