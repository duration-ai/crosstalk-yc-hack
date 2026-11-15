using System;
using System.Collections.Generic;
using System.Linq;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.Slipstream
{
    // The online pieces of the Slipstream HUD: the HOST and JOIN plates before a room, the four
    // big code boxes while typing a code, the room code in the bottom-left corner of the lobby
    // (with BOTS for the host and LEAVE), a name tag floating over every other racer, and a YOU
    // marker over your own car just after GO.
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
        TouchScreenKeyboard keyboard;
        string entry = "";
        string entryKind;
        float entryShownErrorAt = -10f;

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

        // ---- typing a code or a name ----

        void StartEntry(string kind)
        {
            entryKind = kind;
            entry = "";
            if (TouchScreenKeyboard.isSupported)
                keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.ASCIICapable, false, false, false, false, kind == "code" ? "ROOM CODE" : "YOUR NAME", kind == "code" ? 4 : 12);
        }

        void EndEntry()
        {
            entryKind = null;
            if (keyboard != null) keyboard.active = false;
            keyboard = null;
        }

        string Filter(string text)
        {
            var chars = (text ?? "").Where(c => entryKind == "code" ? char.IsLetter(c) : char.IsLetterOrDigit(c) || c == ' ').ToArray();
            var clean = new string(chars);
            if (entryKind == "code") clean = clean.ToUpperInvariant();
            int max = entryKind == "code" ? 4 : 12;
            return clean.Length > max ? clean.Substring(0, max) : clean;
        }

        void OnlineUpdate()
        {
            if (entryKind == null) return;
            if (Race.RoomId != null)
            {
                EndEntry();
                return;
            }
            bool submit = false;
            if (keyboard != null)
            {
                entry = Filter(keyboard.text);
                if (keyboard.status == TouchScreenKeyboard.Status.Done) submit = true;
                else if (keyboard.status == TouchScreenKeyboard.Status.Canceled || keyboard.status == TouchScreenKeyboard.Status.LostFocus)
                {
                    EndEntry();
                    return;
                }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    EndEntry();
                    return;
                }
                foreach (char c in Input.inputString)
                {
                    if (c == '\b') entry = entry.Length > 0 ? entry.Substring(0, entry.Length - 1) : "";
                    else if (c == '\n' || c == '\r') submit = true;
                    else entry = Filter(entry + c);
                }
            }
            if (entryKind == "code" && entry.Length == 4) submit = true;
            if (!submit) return;
            var kind = entryKind;
            var value = entry;
            EndEntry();
            if (kind == "code" && value.Length == 4) Race.Join(value);
            else if (kind == "name") Race.Rename(value);
        }

        // ---- drawing ----

        void Plate(Rect r, string label, Color fill, Color ink, Action act, float size = 18f)
        {
            int index = buttons.Count;
            buttons.Add(new Hit { Rect = r, Act = act });
            bool pressed = platePressed == index;
            if (pressed)
            {
                var p = new Rect(r.x + 2f * s + r.width * 0.02f, r.y + 2f * s + r.height * 0.02f, r.width * 0.96f, r.height * 0.96f);
                Framed(p, ink == Ink ? White : Ink, Ink, 2.4f * s);
                Text(p, label, size, ink == Ink ? Ink : White, TextAnchor.MiddleCenter);
                return;
            }
            Framed(r, fill, Ink, 2.4f * s);
            Text(r, label, size, ink, TextAnchor.MiddleCenter);
        }

        void NameTab(Rect block, Rect bar, Color color, string name, bool mine, string icon)
        {
            Panel(bar, mine ? Ink : Paper);
            Panel(block, color);
            if (icon != null) Icon(new Rect(block.x + block.width * 0.2f, block.y + block.height * 0.08f, block.height * 0.84f, block.height * 0.84f), icon, White);
            Text(new Rect(bar.x + 12f * s, bar.y, bar.width, bar.height), name, 14f, mine ? White : Ink);
        }

        bool ErrorFresh => Time.time - Race.LastErrorAt < 2.5f;

        // Before a room: your name tab, HOST and JOIN, and the code boxes while typing.
        void Menu()
        {
            float y = top + 2f * s;
            var block = new Rect(left, y, 30f * s, 24f * s);
            var bar = new Rect(block.xMax + 3f * s, y, 170f * s, 24f * s);
            bool naming = entryKind == "name";
            NameTab(block, bar, SlotColor("blue"), naming ? entry + (Mathf.Repeat(Time.time, 1f) < 0.5f ? "_" : "") : (Race.MyName ?? "").ToUpperInvariant(), true, "dash");
            buttons.Add(new Hit { Rect = new Rect(block.x, block.y, bar.xMax - block.x, bar.height), Act = () => { if (entryKind == null) StartEntry("name"); } });

            if (entryKind == "code") CodeBoxes(entry, true);
            else if (ErrorFresh && Race.LastError != null)
            {
                var label = Race.LastError == "NOT_FOUND" ? "NO SUCH ROOM" : Race.LastError == "CONFLICT" ? "ROOM CLOSED" : "NO SIGNAL";
                var size = Measure(label, 16f);
                var r = new Rect(Screen.width * 0.5f - size.x * 0.5f - 16f * s, top + 2f * s, size.x + 32f * s, 30f * s);
                Panel(r, Ink);
                Text(r, label, 16f, Sun, TextAnchor.MiddleCenter);
            }

            var host = new Rect(right - 112f * s, bottom - 56f * s, 112f * s, 56f * s);
            var join = new Rect(right - 112f * s - 8f * s, host.y - 52f * s, 112f * s, 42f * s);
            if (Race.Busy)
            {
                Framed(host, Paper, Ink, 2.4f * s);
                Text(host, "...", 22f, Ink, TextAnchor.MiddleCenter);
                return;
            }
            Plate(join, entryKind == "code" ? "CANCEL" : "JOIN", Paper, Ink, () => { if (entryKind == "code") EndEntry(); else StartEntry("code"); }, 18f);
            Plate(host, "HOST", Sun, Ink, () => { EndEntry(); Race.Host(); }, 24f);
        }

        // Four big letter boxes, top centre: the code being typed, or the room's code.
        void CodeBoxes(string code, bool typing)
        {
            float box = 46f * s;
            float gap = 8f * s;
            float x0 = Screen.width * 0.5f - (4f * box + 3f * gap) * 0.5f;
            float shake = ErrorFresh && Time.time - Race.LastErrorAt < 0.4f ? Mathf.Sin(Time.time * 70f) * 6f * s : 0f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x0 + i * (box + gap) + shake, top + 2f * s, box, box * 1.1f);
                bool caret = typing && i == code.Length && Mathf.Repeat(Time.time, 1f) < 0.5f;
                Framed(r, i < code.Length ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.55f), caret ? Sun : Ink, 2.4f * s);
                if (i < code.Length) Text(r, code[i].ToString(), 34f, Sun, TextAnchor.MiddleCenter, 0f);
            }
        }

        // The room's code, big in the bottom-left corner, with BOTS (host, lobby only) and LEAVE.
        void RoomCorner(Room room, bool lobby)
        {
            var code = Race.RoomCode ?? room.code ?? "";
            var size = Measure(code, 40f);
            var panel = new Rect(left, bottom - 50f * s, size.x + 40f * s, 50f * s);
            Panel(panel, Ink);
            Text(new Rect(panel.x + 20f * s, panel.y, size.x, panel.height), code, 40f, Sun, TextAnchor.MiddleLeft, 0f);

            float y = panel.y - 36f * s;
            var leave = new Rect(left + 6f * s, y, 78f * s, 28f * s);
            bool locked = room.status == RoomStatus.Countdown && Race.MsToStart <= 3000f;
            if (!locked) Plate(leave, "LEAVE", Paper, Ink, Race.Leave, 13f);
            if (Race.IsHost && lobby && room.status == RoomStatus.Lobby && !Race.Busy)
            {
                var bots = new Rect(leave.xMax + 10f * s, y, 78f * s, 28f * s);
                bool on = Race.BotsOn;
                Plate(bots, on ? "BOTS ON" : "BOTS", on ? Sun : Paper, Ink, () => Race.SetBots(!on), 13f);
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
                var sp = cam.WorldToScreenPoint(body.position + Vector3.up * 2.6f);
                if (sp.z < 1.5f || sp.z > TagRange) continue;
                float k = Mathf.Clamp(22f / sp.z, 0.55f, 1f);
                var name = (r.displayName ?? "").ToUpperInvariant();
                var textSize = Measure(name, 14f);
                float h = 22f * s * k;
                var block = new Rect(sp.x - (textSize.x * k + 46f * s * k) * 0.5f, Screen.height - sp.y - h, 18f * s * k, h);
                var bar = new Rect(block.xMax + 2f * s * k, block.y, textSize.x * k + 24f * s * k, h);
                Panel(bar, Ink);
                Panel(block, SlotColor(r.color));
                var st = Style(14f * k, TextAnchor.MiddleLeft);
                st.normal.textColor = TextColor(White, 1f);
                GUI.Label(new Rect(bar.x + 10f * s * k, bar.y, bar.width, bar.height), name, st);
            }
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
