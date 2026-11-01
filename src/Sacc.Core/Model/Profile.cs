using System.Text.Json.Serialization;

namespace Sacc.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InputMode
{
    /// <summary>Put the line on the clipboard and press Ctrl+V in the chat box.</summary>
    Paste,
    /// <summary>Type the line character by character as Unicode key events.</summary>
    Type,
}

public sealed class Settings
{
    public string ProcessName { get; set; } = "SCUM";
    public string ChatKey { get; set; } = "T";
    public InputMode InputMode { get; set; } = InputMode.Paste;
    public int FocusDelayMs { get; set; } = 200;
    public int ChatOpenDelayMs { get; set; } = 150;
    public int BeforeEnterDelayMs { get; set; } = 80;
    public int AfterSendDelayMs { get; set; } = 300;
    /// <summary>SCUM keeps chat open after Enter; SACC then opens it once per batch and presses Escape at the end.</summary>
    public bool ChatStaysOpenAfterSend { get; set; } = true;
    public int CloseDelayMs { get; set; } = 150;
    public int ClipboardCaptureTimeoutMs { get; set; } = 2500;
    public bool RestoreClipboard { get; set; } = true;
    public bool ReturnFocusToSacc { get; set; }
    public bool DryRun { get; set; } = true;
    public bool ConfirmDangerous { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    /// <summary>Your own SteamID64, used as the default target for self-commands.</summary>
    public string MySteamId { get; set; } = "";
    public int HistoryLimit { get; set; } = 500;
    /// <summary>Largest amount put in one #SpawnItem line; bigger kit counts are split.</summary>
    public int MaxSpawnPerCommand { get; set; } = 10;
    /// <summary>StackCount sent for "Full". No magazine holds more than 100, and the game caps overflow.</summary>
    public int FullStackCount { get; set; } = 200;
    /// <summary>Spawner: send the Full StackCount for magazines when no StackCount is entered.</summary>
    public bool SpawnerFillMagazines { get; set; } = true;
    /// <summary>Regex identifying magazine codes, for the "Fill magazines" options.</summary>
    public string MagazinePattern { get; set; } = "^(Magazine_.*|M1_Clip)$";
    /// <summary>scum-map.com marker categories shown on the Map tab.</summary>
    public List<int> MapVisibleCategories { get; set; } = new();
    public string MapType { get; set; } = "Regular";
    /// <summary>The Map tab's GM spawn choices, by action key (animals, npcs, zombies, …).</summary>
    public Dictionary<string, GmChoice> Gm { get; set; } = new();
    /// <summary>GM spawn height sources; the closest enabled one within 300 m wins.</summary>
    public bool GmHeightFromMarkers { get; set; } = true;
    public bool GmHeightFromMe { get; set; } = true;
    public bool GmHeightFromPlayers { get; set; } = true;
    public bool GmHeightFromVehicles { get; set; } = true;
    public bool GmHeightFromLocations { get; set; } = true;
    /// <summary>When on, GM spawns use <see cref="GmSetHeight"/> and ignore every other source.</summary>
    public bool GmUseSetHeight { get; set; }
    public double GmSetHeight { get; set; }
    /// <summary>Added to whichever height is chosen (world units, cm), e.g. to drop things from above the ground.</summary>
    public double GmHeightOffset { get; set; }    /// <summary>Z used when teleporting to a clicked map spot that has no known height (scum-map.com uses 0).</summary>
    public double MapTeleportZ { get; set; }
    public GridStyle MapGrid { get; set; } = GridStyle.Sectors;
}

public sealed class PlayerRecord
{
    public string SteamId { get; set; } = "";
    /// <summary>The in-game character name.</summary>
    public string Nickname { get; set; } = "";
    /// <summary>The Steam name ("Platform Name" in #ListPlayers).</summary>
    public string PlatformName { get; set; } = "";
    public long? Fame { get; set; }
    public double? Account { get; set; }
    public long? Gold { get; set; }
    public Vec3? Location { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Nickname) ? SteamId : $"{Nickname}  ({SteamId})";
}

public sealed class GmChoice
{
    public string Type { get; set; } = "";
    public int Count { get; set; } = 1;
}

/// <summary>A vehicle from #ListSpawnedVehicles.</summary>
public sealed class VehicleRecord
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public Vec3 Location { get; set; }
}

/// <summary>A base flag from #ListFlags.</summary>
public sealed class FlagRecord
{
    public string Id { get; set; } = "";
    public string OwnerSteamId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public Vec3 Location { get; set; }

    [JsonIgnore]
    public string Label
    {
        get
        {
            var owner = !string.IsNullOrWhiteSpace(OwnerName) ? OwnerName : OwnerSteamId;
            var id = Id.Length > 0 ? $"Flag {Id}" : "Flag";
            return owner.Length > 0 ? $"{id} · {owner}" : id;
        }
    }
}

public readonly record struct Vec3(double X, double Y, double Z)
{
    public override string ToString() =>
        FormattableString.Invariant($"{X:0.###} {Y:0.###} {Z:0.###}");
}

public sealed class Bookmark
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class Macro
{
    public string Name { get; set; } = "New macro";
    public string Script { get; set; } = "";
}

public sealed class KitItem
{
    /// <summary>Game spawn code; null while an imported SAH id is still unresolved.</summary>
    public string? Code { get; set; }
    public int Amount { get; set; } = 1;
    /// <summary>Optional StackCount (rounds in a mag, units in a stack). Ignored when <see cref="StackFull"/> is set.</summary>
    public int? StackCount { get; set; }
    /// <summary>Spawn with Settings.FullStackCount; the game caps it at the item's real maximum.</summary>
    public bool StackFull { get; set; }
    /// <summary>The item's numeric id in an imported SCUM Admin Helper pack, kept so it can be re-resolved.</summary>
    public int? SahId { get; set; }

    [JsonIgnore]
    public string Display => Code ?? $"(unresolved SAH id {SahId})";
}

public sealed class Kit
{
    public string Name { get; set; } = "New kit";
    /// <summary>Folder-style grouping, e.g. "Event_Loadouts/Nest/Arctic".</summary>
    public string Group { get; set; } = "";
    public string Description { get; set; } = "";
    public List<KitItem> Items { get; set; } = new();
    /// <summary>Non-spawn admin commands run after the items (from text loadouts).</summary>
    public List<string> ExtraCommands { get; set; } = new();
    /// <summary>Spawn every magazine in the kit with the Full StackCount, whatever its own row says.</summary>
    public bool FillMagazines { get; set; } = true;
    /// <summary>Where the kit was imported from (file name, or zip!entry); empty for kits made in SACC.</summary>
    public string Source { get; set; } = "";
    public DateTime? SourceDate { get; set; }

    [JsonIgnore]
    public int UnresolvedCount => Items.Count(i => i.Code == null);

    [JsonIgnore]
    public string Label => SourceDate is { } d ? $"{Name}  ({d:yyyy-MM-dd})" : Name;

    /// <summary>
    /// Deep copy for editing. The clone has no Source, so re-importing the
    /// original file never replaces or duplicates it.
    /// </summary>
    /// <summary>"New kit", or "New kit 2", "New kit 3"… if taken.</summary>
    public static string UniqueName(string baseName, IEnumerable<string> takenNames)
    {
        var taken = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        for (int n = 2; taken.Contains(name); n++) name = $"{baseName} {n}";
        return name;
    }

    /// <summary>
    /// Add an item, or bump the amount of an existing row with the same code and stack
    /// settings (a different StackCount is a different row). Returns the row that holds it.
    /// </summary>
    public KitItem AddItem(string code, int amount, int? stackCount, bool stackFull)
    {
        var stack = stackFull ? null : stackCount is > 0 ? stackCount : null;
        var existing = Items.FirstOrDefault(i =>
            string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase) && i.StackFull == stackFull && i.StackCount == stack);
        if (existing != null)
        {
            existing.Amount += Math.Max(1, amount);
            return existing;
        }
        var item = new KitItem { Code = code, Amount = Math.Max(1, amount), StackCount = stack, StackFull = stackFull };
        Items.Add(item);
        return item;
    }

    public Kit Clone(IEnumerable<string> takenNames)
    {
        var taken = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
        var name = $"{Name} (copy)";
        for (int n = 2; taken.Contains(name); n++) name = $"{Name} (copy {n})";
        return new Kit
        {
            Name = name,
            Group = Group,
            Description = Description,
            Items = Items.Select(i => new KitItem
            {
                Code = i.Code, Amount = i.Amount, SahId = i.SahId, StackCount = i.StackCount, StackFull = i.StackFull,
            }).ToList(),
            ExtraCommands = ExtraCommands.ToList(),
            FillMagazines = FillMagazines,
        };
    }
}

public sealed class HistoryEntry
{
    public DateTime Time { get; set; }
    public string Text { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class Profile
{
    public const int CurrentVersion = 1;
    /// <summary>0 = saved before SACC 0.3.3, which added versioning; <see cref="Migrate"/> brings it up to date.</summary>
    public int Version { get; set; }
    public Settings Settings { get; set; } = new();

    public void Migrate()
    {
        // 1: magazines spawn full by default. Older profiles saved every kit with Fill magazines off.
        if (Version < 1) foreach (var k in Kits) k.FillMagazines = true;
        Version = CurrentVersion;
    }

    public List<PlayerRecord> Players { get; set; } = new();
    /// <summary>When the last #ListPlayers output was read; players seen then were online.</summary>
    public DateTime? PlayersListedAt { get; set; }
    /// <summary>The last #ListFlags result; each read replaces the whole list.</summary>
    public List<FlagRecord> Flags { get; set; } = new();
    public DateTime? FlagsListedAt { get; set; }
    /// <summary>The last #ListSpawnedVehicles result; used for GM spawn heights.</summary>
    public List<VehicleRecord> Vehicles { get; set; } = new();
    public DateTime? VehiclesListedAt { get; set; }
    public List<Bookmark> Bookmarks { get; set; } = new();
    public List<Macro> Macros { get; set; } = new();
    public List<Kit> Kits { get; set; } = new();
    public List<string> FavoriteItems { get; set; } = new();
    public List<string> FavoriteVehicles { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = new();
}
