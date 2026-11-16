using System;
using System.Collections.Generic;
using Dogpatch.Race;
using UnityEngine;

namespace Dogpatch.WorldChanges
{
    [Serializable]
    public class WorldChangeOption
    {
        public string keywords;
        public WorldChangeParams @params;
        public string news;
        public string line;
        public bool roulette;
    }

    [Serializable]
    public class WorldChangeDef
    {
        public string type;
        public string pictogram;
        public string param;
        public float seconds;
        public WorldChangeOption[] options;
    }

    [Serializable]
    public class NamedColor
    {
        public string name;
        public string hex;
    }

    [Serializable]
    public class WorldChangeCatalogData
    {
        public WorldChangeDef[] changes;
        public NamedColor[] colors;
        public string[] roulette;
    }

    // The world changes as data (Resources/SpeedDial/world-changes.json): each type's pictogram,
    // how long its effect lasts, its param values, the news line and the Operator's line. The local
    // session, the local Operator, the bots and the effects all read it, so adding a variant is a
    // data change.
    public sealed class WorldChangeCatalog
    {
        readonly WorldChangeCatalogData data;
        readonly Dictionary<string, WorldChangeDef> byType = new Dictionary<string, WorldChangeDef>();

        public IReadOnlyList<WorldChangeDef> All => data.changes;

        public WorldChangeCatalog(string json)
        {
            data = JsonUtility.FromJson<WorldChangeCatalogData>(json);
            foreach (var def in data.changes) byType[def.type] = def;
        }

        public static WorldChangeCatalog Load() => new WorldChangeCatalog(Resources.Load<TextAsset>("SpeedDial/world-changes").text);

        public WorldChangeDef Get(string type) => type != null && byType.TryGetValue(type, out var def) ? def : null;

        public string Hex(string colorName)
        {
            foreach (var c in data.colors)
                if (c.name == colorName) return c.hex;
            return null;
        }

        public static string Field(WorldChangeParams p, string name)
        {
            if (p == null) return null;
            switch (name)
            {
                case "level": return p.level;
                case "timeOfDay": return p.timeOfDay;
                case "color": return p.color;
                case "size": return p.size;
                default: return null;
            }
        }

        public WorldChangeOption Option(WorldChangeCommand command)
        {
            var def = Get(command?.type);
            if (def == null) return null;
            if (string.IsNullOrEmpty(def.param)) return def.options[0];
            string value = Field(command.@params, def.param);
            foreach (var option in def.options)
                if (Field(option.@params, def.param) == value) return option;
            return null;
        }

        // Params-level validation shared with the server: unknown type, bad value, count out of range.
        public bool Validate(WorldChangeCommand command, out string error)
        {
            error = null;
            var def = Get(command?.type);
            if (def == null)
            {
                error = "Unknown world change type " + command?.type;
                return false;
            }
            if (Option(command) == null)
            {
                error = $"Bad {def.param} for {def.type}";
                return false;
            }
            if (def.type == "recolor" && Hex(command.@params.color) == null)
            {
                error = "Unknown color " + command.@params.color;
                return false;
            }
            if (def.type == "ramp" && (command.@params == null || command.@params.count < 1 || command.@params.count > 5))
            {
                error = "Ramp count must be 1 to 5";
                return false;
            }
            if (def.type == "swap" && string.IsNullOrEmpty(command.@params?.targetPlayerId))
            {
                error = "Swap needs a targetPlayerId";
                return false;
            }
            return true;
        }

        public static string ShortName(string displayName)
        {
            var name = (displayName ?? "").Trim();
            int space = name.IndexOf(' ');
            return (space > 0 ? name.Substring(0, space) : name).ToUpperInvariant();
        }

        public string News(WorldChangeCommand command, string firer, string target)
        {
            var option = Option(command);
            if (option == null) return "";
            return option.news
                .Replace("{name}", ShortName(firer))
                .Replace("{target}", ShortName(target))
                .Replace("{color}", (command.@params?.color ?? "").ToUpperInvariant());
        }

        public string Line(WorldChangeCommand command) => Option(command)?.line ?? "Done, dude!";

        public static WorldChangeCommand Command(string type, WorldChangeParams p) => new WorldChangeCommand { type = type, @params = Copy(p) };

        public static WorldChangeParams Copy(WorldChangeParams p) =>
            p == null ? new WorldChangeParams() : JsonUtility.FromJson<WorldChangeParams>(JsonUtility.ToJson(p));

        // The order the local Operator works through when a call carries no words it understands.
        public List<WorldChangeCommand> Roulette()
        {
            var list = new List<WorldChangeCommand>();
            foreach (var entry in data.roulette)
            {
                var parts = entry.Split(':');
                var def = Get(parts[0]);
                if (def == null) continue;
                foreach (var option in def.options)
                {
                    if (!string.IsNullOrEmpty(def.param) && Field(option.@params, def.param) != parts[1]) continue;
                    list.Add(Command(def.type, option.@params));
                    break;
                }
            }
            return list;
        }

        // Keyword matching, the same idea as the backend's keyword provider: the option whose
        // keywords appear most often in what the caller said, or null.
        public WorldChangeCommand Match(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var words = text.ToLowerInvariant().Split(new[] { ' ', ',', '.', '!', '?', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            WorldChangeCommand best = null;
            int bestScore = 0;
            foreach (var def in data.changes)
                foreach (var option in def.options)
                {
                    int score = 0;
                    foreach (var keyword in option.keywords.Split(' '))
                        foreach (var word in words)
                            if (word == keyword) score++;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = Command(def.type, option.@params);
                }
            return best;
        }
    }
}
