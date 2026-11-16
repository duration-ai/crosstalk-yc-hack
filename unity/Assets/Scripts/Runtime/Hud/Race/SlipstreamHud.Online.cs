using System;
using System.Collections.Generic;
using System.Linq;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The online pieces of the Slipstream HUD: the mission plate over the ring lobby ("RACE: IRON & STEAM",
    // waiting for more racers), a name tag floating over every other player in the shared world
    // and every other racer, a YOU marker over your own car just after GO, and a small RECONNECTING
    // chip while the server can't be reached.
    public sealed partial class SlipstreamHud
    {
        struct Hit
        {
            public Rect Rect;
            public Action Act;
        }

        readonly List<Hit> buttons = new List<Hit>();
        int plateFinger = int.MinValue;
        int platePressed = -1;

        bool OnlineHit(Vector2 p) => buttons.Any(b => Inflate(b.Rect, 8f * s).Contains(p));

        bool OnlinePress(int finger, Vector2 p)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                if (!Inflate(buttons[i].Rect, 8f * s).Contains(p)) continue;
                plateFinger = finger;
                platePressed = i;
                return true;
            }
            return false;
        }

        bool OnlineRelease(int finger, Vector2 p)
        {
            if (finger != plateFinger) return false;
            int i = platePressed;
            plateFinger = int.MinValue;
            platePressed = -1;
            if (i >= 0 && i < buttons.Count && Inflate(buttons[i].Rect, 12f * s).Contains(p)) buttons[i].Act();
            return true;
        }

        // Top centre, under the countdown: the mission's name and how many more racers it needs or how long
        // until it starts, or while
        // the route's last race is still running, who's in it and roughly when it ends.
        void MissionPlate(Room room)
        {
            if (room.status != RoomStatus.Lobby && room.status != RoomStatus.Countdown) return;
            int unready = room.playerCount - room.readyCount;
            var running = Race.Running;
            long endsIn = running == null ? 0 : (running.endsAt - Race.NowMs + 999) / 1000;
            var label = running != null
                ? "RACE IN PROGRESS: " + string.Join(", ", running.racers.Select(r => r.displayName)) + " racing  ·  " + (endsIn > 0 ? "ends in ~" + endsIn + "s" : "ending now") + ". You're first in the next lobby"
                : "RACE: " + Race.RouteName(Race.RouteId) + (room.status == RoomStatus.Countdown ? "  ·  STARTING IN " + Mathf.CeilToInt(Race.MsToStart / 1000f) + "s" : room.playerCount < 2 ? "  ·  WAITING FOR RACERS" : unready > 0 ? "  ·  WAITING FOR " + unready + " TO READY" : "  ·  ALL READY");
            var size = Measure(label, 16f);
            var r = new Rect(Screen.width * 0.5f - size.x * 0.5f - 18f * s, top + 2f * s, size.x + 36f * s, 32f * s);
            Panel(r, Ink);
            Text(r, label, 16f, Sun, TextAnchor.MiddleCenter);
        }

        // Top centre while requests are failing; the world keeps its last known state meanwhile.
        void NetChip()
        {
            if (!Race.Reconnecting) return;
            const string label = "RECONNECTING";
            var size = Measure(label, 13f);
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            var r = new Rect(Screen.width * 0.5f - size.x * 0.5f - 14f * s, top + 40f * s, size.x + 28f * s, 24f * s);
            Panel(r, new Color(Ink.r, Ink.g, Ink.b, 0.9f));
            Text(r, label, 13f, White, TextAnchor.MiddleCenter, 0f, pulse);
        }

        // Everyone else in the shared world who isn't racing with you, in their world colour.
        void WorldTags()
        {
            var cam = Race.Game.View;
            foreach (var remote in Race.Remotes.All)
            {
                if (remote.InRace && Race.Last != null) continue;
                var body = remote.Body;
                if (body == null || !body.gameObject.activeInHierarchy) continue;
                Tag(cam, body, remote.Name + BuildNote(remote), remote.WorldColor);
            }
        }

        const float TagRange = 80f;

        // A small name tag over every other racer, bots and remote players alike: a slanted ink plate
        // with their colour block and name, scaled with distance and hidden past 80 m. Bots get one
        // from the grid on; remote players, who may be anywhere, always do.
        void NameTags()
        {
            if (Race.Last == null) return;
            var cam = Race.Game.View;
            foreach (var r in Race.Last.racers)
            {
                if (r.left || r.playerId == Race.MyId) continue;
                if (!Race.Gridded && !Race.IsRemote(r.playerId)) continue;
                var body = Race.BodyOf(r.playerId);
                if (body == null || !body.gameObject.activeInHierarchy) continue;
                Tag(cam, body, r.displayName + BuildNote(Race.Remotes.Get(r.playerId)), r.color);
            }
        }

        // Players whose presence speaks another protocol get flagged, so a build mismatch is visible.
        static string BuildNote(RemoteRacers.Remote remote) =>
            remote == null || remote.WorldSeenAt < 0f || remote.Protocol == ConvexRaceSession.Protocol ? "" : remote.Protocol < ConvexRaceSession.Protocol ? "  OLD BUILD" : "  NEW BUILD";

        void Tag(Camera cam, Transform body, string displayName, string color)
        {
            var sp = cam.WorldToScreenPoint(body.position + Vector3.up * 2.6f);
            if (sp.z < 1.5f || sp.z > TagRange) return;
            float k = Mathf.Clamp(22f / sp.z, 0.55f, 1f);
            var name = (displayName ?? "").ToUpperInvariant();
            var textSize = Measure(name, 14f);
            float h = 22f * s * k;
            var block = new Rect(sp.x - (textSize.x * k + 46f * s * k) * 0.5f, Screen.height - sp.y - h, 18f * s * k, h);
            var bar = new Rect(block.xMax + 2f * s * k, block.y, textSize.x * k + 24f * s * k, h);
            Panel(bar, Ink);
            Panel(block, SlotColor(color));
            var st = Style(14f * k, TextAnchor.MiddleLeft);
            st.normal.textColor = TextColor(White, 1f);
            GUI.Label(new Rect(bar.x + 10f * s * k, bar.y, bar.width, bar.height), name, st);
        }

        const float FindMeSeconds = 4f;

        // For the first seconds after GO, a sunshine YOU marker over your own car, so you can find
        // yourself on a grid of look-alike cars.
        void FindMe()
        {
            float since = Time.time - Race.StartedAt;
            if (since > FindMeSeconds || Race.MyCar == null) return;
            var sp = Race.Game.View.WorldToScreenPoint(Race.MyCar.transform.position + Vector3.up * 2.8f);
            if (sp.z < 1f) return;
            float alpha = Mathf.Clamp01((FindMeSeconds - since) / 0.5f);
            float bob = Mathf.Sin(since * 7f) * 3f * s;
            var size = Measure("YOU", 16f);
            var plate = new Rect(sp.x - (size.x + 26f * s) * 0.5f, Screen.height - sp.y - 30f * s + bob, size.x + 26f * s, 26f * s);
            Panel(Inflate(plate, 1.6f * s), new Color(Ink.r, Ink.g, Ink.b, alpha));
            Panel(plate, new Color(Sun.r, Sun.g, Sun.b, alpha));
            Text(plate, "YOU", 16f, Ink, TextAnchor.MiddleCenter, 0f, alpha);
            Panel(new Rect(plate.center.x - 3f * s, plate.yMax + 3f * s, 6f * s, 9f * s), new Color(Sun.r, Sun.g, Sun.b, alpha), 0f);
        }
    }
}
