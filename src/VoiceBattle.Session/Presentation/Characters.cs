using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VoiceBattle.Session.Presentation
{
    public sealed record Localized(string Ar, string En)
    {
        public string Get(bool arabic) => arabic ? Ar : En;
    }

    /// <summary>An sRGB colour parsed from "#RRGGBB".</summary>
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb Parse(string hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#'
                || !int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
                throw new FormatException($"colour '{hex}' must be #RRGGBB");
            return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    }

    public sealed record Palette(Rgb Primary, Rgb Secondary, Rgb Accent);

    /// <summary>A playable character. Purely cosmetic — the battle never sees it (GDD §10).</summary>
    public sealed record Character(
        string Id, Localized Name, Localized Tagline,
        string Body, string Head, string Eyes, string Accessory, string Idle,
        Palette Palette, Localized Taunt, Localized Win, Localized Lose,
        float PreviewTranspose, float PreviewVibratoCents);

    /// <summary>data/characters.json, validated.</summary>
    public sealed class CharacterRoster
    {
        public static readonly string[] Bodies = { "round", "tall", "square", "pear" };
        public static readonly string[] Heads = { "round", "oval", "square" };
        public static readonly string[] EyeStyles = { "dots", "big", "sleepy", "visor" };
        public static readonly string[] Accessories = { "none", "mustache", "sunglasses", "antenna", "cat_ears", "glasses_bun", "chef_hat" };
        public static readonly string[] Idles = { "bob", "sway", "bounce", "jitter" };

        /// <summary>Keys that would make a character more than a skin. Their presence anywhere rejects the file.</summary>
        public static readonly string[] ForbiddenKeys =
        {
            "hp", "max_hp", "damage", "base_damage", "charge", "charge_gain", "cooldown", "cooldown_ms",
            "threshold", "accuracy", "multiplier", "armor", "attack", "defense", "speed", "tempo",
        };

        private readonly List<Character> _all;

        private CharacterRoster(List<Character> all) => _all = all;

        public IReadOnlyList<Character> All => _all;

        public Character Get(string id) => _all.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException($"no character '{id}'");

        public static CharacterRoster Load(string path) => Parse(File.ReadAllText(path));

        public static CharacterRoster Parse(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var errors = new List<string>();
            FindForbidden(doc.RootElement, "", errors);

            var list = new List<Character>();
            if (!doc.RootElement.TryGetProperty("characters", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                errors.Add("characters.json needs a non-empty \"characters\" array");
            else
            {
                foreach (var c in arr.EnumerateArray())
                {
                    string id = Str(c, "id");
                    string where = id.Length > 0 ? id : $"character #{list.Count + 1}";
                    try
                    {
                        var pal = c.GetProperty("palette");
                        var lines = c.GetProperty("lines");
                        var pv = c.TryGetProperty("preview_voice", out var p) ? p : default;
                        list.Add(new Character(
                            id, Loc(c, "name", where, errors), Loc(c, "tagline", where, errors),
                            OneOf(c, "body", Bodies, where, errors), OneOf(c, "head", Heads, where, errors),
                            OneOf(c, "eyes", EyeStyles, where, errors), OneOf(c, "accessory", Accessories, where, errors),
                            OneOf(c, "idle", Idles, where, errors),
                            new Palette(Rgb.Parse(Str(pal, "primary")), Rgb.Parse(Str(pal, "secondary")), Rgb.Parse(Str(pal, "accent"))),
                            Loc(lines, "taunt", where, errors), Loc(lines, "win", where, errors), Loc(lines, "lose", where, errors),
                            pv.ValueKind == JsonValueKind.Object && pv.TryGetProperty("transpose", out var t) ? (float)t.GetDouble() : 0f,
                            pv.ValueKind == JsonValueKind.Object && pv.TryGetProperty("vibrato_cents", out var v) ? (float)v.GetDouble() : 25f));
                        if (id.Length == 0) errors.Add($"{where}: missing id");
                    }
                    catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException)
                    {
                        errors.Add($"{where}: {ex.Message}");
                    }
                }
                foreach (var dup in list.GroupBy(x => x.Id).Where(g => g.Count() > 1)) errors.Add($"duplicate character id '{dup.Key}'");
            }

            if (errors.Count > 0) throw new FormatException("characters.json: " + string.Join("; ", errors));
            return new CharacterRoster(list);
        }

        private static void FindForbidden(JsonElement e, string path, List<string> errors)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (ForbiddenKeys.Contains(p.Name))
                        errors.Add($"'{path}{p.Name}' looks like a gameplay stat — characters are cosmetic only (GDD §10)");
                    FindForbidden(p.Value, path + p.Name + ".", errors);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var x in e.EnumerateArray()) FindForbidden(x, $"{path}[{i++}].", errors);
            }
        }

        private static string OneOf(JsonElement e, string name, string[] allowed, string where, List<string> errors)
        {
            string v = Str(e, name);
            if (!allowed.Contains(v)) errors.Add($"{where}: {name} '{v}' must be one of {string.Join("/", allowed)}");
            return v;
        }

        private static Localized Loc(JsonElement e, string name, string where, List<string> errors)
        {
            if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Object) { errors.Add($"{where}: missing {name}"); return new Localized("", ""); }
            var l = new Localized(Str(p, "ar"), Str(p, "en"));
            if (l.Ar.Length == 0 || l.En.Length == 0) errors.Add($"{where}: {name} needs both ar and en");
            return l;
        }

        private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    }
}
