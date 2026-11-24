using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sacc.Core;

/// <summary>
/// Optional per-item metadata — the facts SCUM's flat <c>#ListItems</c> list doesn't give: a readable name and
/// description, weight, inventory grid size, rarity, trader buy/sell prices, the fame needed to buy it, and which
/// DLC it belongs to. Keyed by spawn code. Every field is optional; a build may bundle a metadata file or the
/// user may generate one, and no file simply means no metadata (the Spawner still works on codes alone).
///
/// Clean-room: this is the game's own item data, on the same footing as the item icons and the terrain grid
/// (see CLEANROOM.md). It is never taken from another admin tool's bundled table.
/// </summary>
public sealed record ItemMeta
{
    public string Code { get; init; } = "";
    public string? Name { get; init; }
    public string? Description { get; init; }
    /// <summary>Weight in kilograms.</summary>
    public double? WeightKg { get; init; }
    /// <summary>Inventory grid footprint in cells.</summary>
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Rarity { get; init; }
    /// <summary>Default trader price to buy (currency units).</summary>
    public double? TraderBuy { get; init; }
    /// <summary>Default trader price to sell.</summary>
    public double? TraderSell { get; init; }
    /// <summary>Fame points required to buy from a trader.</summary>
    public long? FameToBuy { get; init; }
    /// <summary>DLC pack name, or null for the base game.</summary>
    public string? Dlc { get; init; }
}

/// <summary>
/// A lookup of <see cref="ItemMeta"/> by spawn code, loaded from <c>game-item-meta.json</c>. The file is
/// <c>{ "source": "...", "readAt": "...", "items": { "&lt;code&gt;": { ...fields } } }</c>. Missing, empty or
/// unreadable files yield <see cref="Empty"/> rather than throwing, so metadata is always a bonus, never a
/// requirement.
/// </summary>
public sealed class ItemMetaTable
{
    public static readonly ItemMetaTable Empty = new(new Dictionary<string, ItemMeta>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, ItemMeta> _byCode;

    public ItemMetaTable(IReadOnlyDictionary<string, ItemMeta> byCode) => _byCode = byCode;

    public int Count => _byCode.Count;

    /// <summary>The metadata for a code, or null when the file has none for it.</summary>
    public ItemMeta? Get(string? code) =>
        !string.IsNullOrWhiteSpace(code) && _byCode.TryGetValue(code.Trim(), out var m) ? m : null;

    public sealed class FileModel
    {
        public string? Source { get; set; }
        public string? ReadAt { get; set; }
        public Dictionary<string, ItemMeta> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ItemMetaTable Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Empty;
        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), Options);
            if (model?.Items is not { Count: > 0 } items) return Empty;
            var dict = new Dictionary<string, ItemMeta>(StringComparer.OrdinalIgnoreCase);
            foreach (var (code, meta) in items)
                if (!string.IsNullOrWhiteSpace(code))
                    dict[code.Trim()] = meta with { Code = code.Trim() };
            return new ItemMetaTable(dict);
        }
        catch
        {
            return Empty; // a malformed metadata file just means "no metadata"
        }
    }
}
