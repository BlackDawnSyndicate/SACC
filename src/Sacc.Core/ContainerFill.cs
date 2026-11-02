using System.Globalization;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>
/// "#SpawnInventoryFullOf Hiking_Backpack_01_02 0 12_Gauge_Slug Soap": a container the game fills to its
/// space and weight limit with stacks of the listed items. SACC builds the line from a <see cref="ContainerPreset"/>.
/// </summary>
public static partial class ContainerFill
{
    public const string CatalogId = "spawn-inventory-full-of";

    /// <summary>Item codes in typed or pasted text: anything separated by spaces, commas or line breaks.</summary>
    public static List<string> SplitCodes(string? text) =>
        Separators().Split(text ?? "").Where(s => s.Length > 0).ToList();

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex Separators();

    /// <summary>Values for the catalog entry's {container} {option} {items}.</summary>
    public static Dictionary<string, string?> Values(ContainerPreset p) => new()
    {
        ["container"] = p.Container,
        ["option"] = p.Option,
        ["items"] = string.Join(" ", p.Items),
    };

    [GeneratedRegex(@"^\s*#SpawnInventoryFullOf\s+(\S+)\s+(-?\d+)\s+(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Line();

    /// <summary>Reads a "#SpawnInventoryFullOf container n item item…" line back into a preset; null if it isn't one.</summary>
    public static ContainerPreset? Parse(string? line)
    {
        var m = Line().Match(line ?? "");
        if (!m.Success) return null;
        return new ContainerPreset
        {
            Container = m.Groups[1].Value,
            Option = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            Items = SplitCodes(m.Groups[3].Value),
        };
    }
}
