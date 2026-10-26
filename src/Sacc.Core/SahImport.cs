using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>
/// Reads files exported by SCUM Admin Helper (SAH) into SACC's own schemas:
/// pack JSON ("SAH Pack") → kits, players JSON → player records, and plain
/// "#spawnitem" text loadouts → kits. Formats come from the user's own exports;
/// see CLEANROOM.md for where the item-id table comes from.
/// </summary>
public static partial class SahImport
{
    public sealed class Result
    {
        public List<Kit> Kits { get; } = new();
        public List<PlayerRecord> Players { get; } = new();
        public List<string> Warnings { get; } = new();
        public int FilesRead { get; set; }
        public int FilesSkipped { get; set; }

        public string Summary =>
            $"{Kits.Count} kit(s), {Players.Count} player(s) from {FilesRead} file(s)"
            + (FilesSkipped > 0 ? $", {FilesSkipped} skipped" : "")
            + (Kits.Sum(k => k.UnresolvedCount) is var u and > 0 ? $", {u} unresolved item(s)" : "")
            + ".";
    }

    // SAH stamps exports with "(dd-MM-yyyy)" in the file name.
    [GeneratedRegex(@"\((\d{2}-\d{2}-\d{4})\)")]
    private static partial Regex FileDate();

    [GeneratedRegex(@"^#spawnitem\s+(\S+)(?:\s+(\d+))?(?:\s+StackCount\s+(\d+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SpawnLine();

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex SteamId64();

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    // ------------------------------------------------------------ entry points

    /// <summary>Import every supported file under a folder (recursively, including inside .zip files).</summary>
    public static Result ImportFolder(string folder, IReadOnlyDictionary<int, string> itemMap)
    {
        var result = new Result();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var group = RelativeGroup(folder, Path.GetDirectoryName(path)!);
            ImportPath(path, group, itemMap, result);
        }
        return result;
    }

    public static Result ImportFiles(IEnumerable<string> paths, IReadOnlyDictionary<int, string> itemMap)
    {
        var result = new Result();
        foreach (var path in paths) ImportPath(path, "", itemMap, result);
        return result;
    }

    private static void ImportPath(string path, string group, IReadOnlyDictionary<int, string> itemMap, Result result)
    {
        var name = Path.GetFileName(path);
        try
        {
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries.Where(e => e.Length > 0).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(entry.Open());
                    var entryDir = Path.GetDirectoryName(entry.FullName)?.Replace('\\', '/') ?? "";
                    var entryGroup = JoinGroup(group, Path.GetFileNameWithoutExtension(name), entryDir);
                    ImportText(reader.ReadToEnd(), $"{name}!{entry.FullName}", entryGroup, itemMap, result);
                }
                return;
            }
            ImportText(File.ReadAllText(path), name, group, itemMap, result);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            result.FilesSkipped++;
            result.Warnings.Add($"{name}: {ex.Message}");
        }
    }

    /// <summary>Detect the file type from its content and import it.</summary>
    public static void ImportText(string text, string sourceName, string group, IReadOnlyDictionary<int, string> itemMap, Result result)
    {
        var ext = Path.GetExtension(sourceName).ToLowerInvariant();
        if (ext == ".json")
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(text, DocOptions); }
            catch (JsonException ex)
            {
                result.FilesSkipped++;
                result.Warnings.Add($"{sourceName}: not valid JSON ({ex.Message})");
                return;
            }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && TryGet(root, "Packages", out var packages))
                {
                    result.Kits.AddRange(ReadPacks(packages, sourceName, group, itemMap, result.Warnings));
                    result.FilesRead++;
                    return;
                }
                if (root.ValueKind == JsonValueKind.Object && TryGet(root, "Players", out var players))
                {
                    result.Players.AddRange(ReadPlayers(players, DateFromName(sourceName), sourceName, result.Warnings));
                    result.FilesRead++;
                    return;
                }
            }
            result.FilesSkipped++;
            result.Warnings.Add($"{sourceName}: JSON is not an SAH pack or players export.");
            return;
        }

        if (ext is ".txt" or "" && text.Contains("#spawnitem", StringComparison.OrdinalIgnoreCase))
        {
            result.Kits.AddRange(ReadLoadoutText(text, sourceName, group));
            result.FilesRead++;
            return;
        }

        result.FilesSkipped++;
    }

    // ------------------------------------------------------------ packs

    public static List<Kit> ReadPacks(JsonElement packages, string sourceName, string group,
        IReadOnlyDictionary<int, string> itemMap, List<string> warnings)
    {
        var kits = new List<Kit>();
        if (packages.ValueKind != JsonValueKind.Array) return kits;
        var date = DateFromName(sourceName);

        foreach (var p in packages.EnumerateArray())
        {
            var kit = new Kit
            {
                Name = Str(p, "Title") is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(sourceName),
                Description = Str(p, "Description") ?? "",
                Group = group,
                Source = sourceName,
                SourceDate = date,
            };

            if (TryGet(p, "Items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in items.EnumerateObject())
                {
                    if (!int.TryParse(prop.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    {
                        warnings.Add($"{sourceName}: '{kit.Name}' has a non-numeric item key '{prop.Name}'; skipped.");
                        continue;
                    }
                    var amount = prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n) ? n : 1;
                    kit.Items.Add(new KitItem
                    {
                        SahId = id,
                        Code = itemMap.TryGetValue(id, out var code) ? code : null,
                        Amount = Math.Max(1, amount),
                    });
                }
            }

            // Observed null in every export so far; accept a list of strings if SAH ever fills it.
            if (TryGet(p, "Commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
                foreach (var c in commands.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.String && CommandLine.Check(c.GetString()) is { Ok: true } ok)
                        kit.ExtraCommands.Add(ok.Text);

            kits.Add(kit);
        }
        return kits;
    }

    /// <summary>Fill in codes for items imported before the id table knew them (or after it changed).</summary>
    public static int Resolve(IEnumerable<Kit> kits, IReadOnlyDictionary<int, string> itemMap)
    {
        int resolved = 0;
        foreach (var item in kits.SelectMany(k => k.Items))
        {
            if (item.SahId is not { } id || !itemMap.TryGetValue(id, out var code) || item.Code == code) continue;
            if (item.Code == null) resolved++;
            item.Code = code;
        }
        return resolved;
    }

    // ------------------------------------------------------------ players

    public static List<PlayerRecord> ReadPlayers(JsonElement players, DateTime? seen, string sourceName, List<string> warnings)
    {
        var list = new List<PlayerRecord>();
        if (players.ValueKind != JsonValueKind.Array) return list;
        int bad = 0;
        foreach (var p in players.EnumerateArray())
        {
            var steamId = Str(p, "SteamID")?.Trim() ?? "";
            if (!SteamId64().IsMatch(steamId)) { bad++; continue; }
            var rec = new PlayerRecord
            {
                SteamId = steamId,
                Nickname = Str(p, "Nickname")?.Trim() ?? "",
                Fame = Long(Str(p, "Fame")),
                Gold = Long(Str(p, "Gold")),
                Account = Double(Str(p, "Account")),
                LastSeen = seen ?? DateTime.MinValue,
            };
            if (Parsing.TryParseLocation(Str(p, "Location"), out var v)) rec.Location = v;
            list.Add(rec);
        }
        if (bad > 0) warnings.Add($"{sourceName}: {bad} player(s) without a valid SteamID64 skipped.");
        return list;
    }

    /// <summary>Merge imported players into a list, keeping known values the import lacks.</summary>
    public static void MergePlayers(IList<PlayerRecord> target, IEnumerable<PlayerRecord> incoming)
    {
        foreach (var p in incoming)
        {
            var index = -1;
            for (int i = 0; i < target.Count; i++)
                if (target[i].SteamId == p.SteamId) { index = i; break; }
            if (index < 0) { target.Add(p); continue; }

            var existing = target[index];
            // An older export must not overwrite fresher data.
            var (newer, older) = p.LastSeen >= existing.LastSeen ? (p, existing) : (existing, p);
            target[index] = new PlayerRecord
            {
                SteamId = p.SteamId,
                Nickname = string.IsNullOrWhiteSpace(newer.Nickname) ? older.Nickname : newer.Nickname,
                Fame = newer.Fame ?? older.Fame,
                Account = newer.Account ?? older.Account,
                Gold = newer.Gold ?? older.Gold,
                Location = newer.Location ?? older.Location,
                LastSeen = newer.LastSeen,
            };
        }
    }

    // ------------------------------------------------------------ text loadouts

    /// <summary>
    /// Plain-text loadouts: a title line, then "#spawnitem code [n]" lines; any other
    /// '#' line is kept as an extra command. A new non-'#' line starts the next kit.
    /// </summary>
    public static List<Kit> ReadLoadoutText(string text, string sourceName, string group)
    {
        var kits = new List<Kit>();
        Kit? current = null;
        var date = DateFromName(sourceName);

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (!line.StartsWith('#'))
            {
                current = new Kit { Name = line, Group = group, Source = sourceName, SourceDate = date };
                kits.Add(current);
                continue;
            }

            if (current == null)
            {
                current = new Kit { Name = Path.GetFileNameWithoutExtension(sourceName), Group = group, Source = sourceName, SourceDate = date };
                kits.Add(current);
            }

            var spawn = SpawnLine().Match(line);
            if (spawn.Success)
            {
                var amount = spawn.Groups[2].Success ? int.Parse(spawn.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
                current.Items.Add(new KitItem
                {
                    Code = spawn.Groups[1].Value,
                    Amount = Math.Max(1, amount),
                    StackCount = spawn.Groups[3].Success ? int.Parse(spawn.Groups[3].Value, CultureInfo.InvariantCulture) : null,
                });
            }
            else if (CommandLine.Check(line) is { Ok: true } ok)
            {
                current.ExtraCommands.Add(ok.Text);
            }
        }
        return kits.Where(k => k.Items.Count > 0 || k.ExtraCommands.Count > 0).ToList();
    }

    // ------------------------------------------------------------ item id table

    /// <summary>
    /// Load a flat {"sahId": value} table. A value is either a bare item spawn code
    /// ("Weapon_M9") or, for non-item entries such as vehicles and zombies, a complete
    /// admin command ("#spawnvehicle BP_SUV_01_A"). Missing file → empty table.
    /// </summary>
    public static Dictionary<int, string> LoadItemMap(string path)
    {
        var map = new Dictionary<int, string>();
        if (!File.Exists(path)) return map;
        using var doc = JsonDocument.Parse(File.ReadAllText(path), DocOptions);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || prop.Value.ValueKind != JsonValueKind.String
                || prop.Value.GetString() is not { Length: > 0 } value)
                continue;
            if (value.StartsWith('#') ? CommandLine.Check(value).Ok : !value.Any(char.IsWhiteSpace))
                map[id] = value;
        }
        return map;
    }

    /// <summary>
    /// Expand a kit into chat lines. Item codes render through <paramref name="renderSpawn"/>
    /// (code, amount, stack count or null) with amounts split into chunks of at most
    /// <paramref name="maxPerCommand"/> (SAH did the same: 23 → 10, 10, 3). "Full" items use
    /// <paramref name="fullStackCount"/>. Full-command entries are sent once per unit. Extra commands follow.
    /// </summary>
    public static List<string> KitLines(Kit kit, Func<string, int, int?, string> renderSpawn, int maxPerCommand,
        int fullStackCount, out int unresolved)
    {
        unresolved = 0;
        var chunk = Math.Max(1, maxPerCommand);
        var lines = new List<string>();
        foreach (var item in kit.Items)
        {
            if (item.Code == null) { unresolved++; continue; }
            if (item.Code.StartsWith('#'))
            {
                for (int i = 0; i < item.Amount; i++) lines.Add(item.Code);
                continue;
            }
            var stack = item.StackFull ? fullStackCount : item.StackCount is > 0 ? item.StackCount : null;
            for (var left = item.Amount; left > 0; left -= chunk)
                lines.Add(renderSpawn(item.Code, Math.Min(chunk, left), stack));
        }
        lines.AddRange(kit.ExtraCommands);
        return lines;
    }

    // ------------------------------------------------------------ helpers

    public static DateTime? DateFromName(string name)
    {
        var m = FileDate().Match(name);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    private static string RelativeGroup(string root, string dir)
    {
        var rel = Path.GetRelativePath(root, dir);
        return rel == "." ? "" : rel.Replace('\\', '/');
    }

    private static string JoinGroup(params string[] parts) =>
        string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        foreach (var prop in e.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) { value = prop.Value; return true; }
        value = default;
        return false;
    }

    private static string? Str(JsonElement e, string name) =>
        TryGet(e, name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static long? Long(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? Double(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
