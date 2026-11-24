using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ScumStudio.Assets.Catalog;
using ScumStudio.Level.Editing;
using ScumStudio.Modding.Catalog;
using ScumStudio.Modding.Tuning;

namespace ScumStudio.Cli.Commands;

/// <summary>
/// <c>scumstudio asset items &lt;source&gt; -o &lt;file&gt;</c>: reads the stock item catalogue and writes the per-item
/// metadata SACC enriches its spawner with (friendly name, description, weight, inventory size, trader buy/sell price and
/// fame-to-buy), as a small JSON file keyed by the item's spawn code. Item attributes come from each item's package and
/// its <c>_ES</c> entity setup (<see cref="AssetEditing.ReadValues"/>); the trade numbers come once from the economy
/// DataTable <c>Table_TradeableDesc</c> (<see cref="DataTableEdits.TradeableTable"/>), whose rows are named after the
/// item class (<c>Weapon_AK47_C</c>). It is the sibling of <c>asset heights</c> and <c>asset map-features</c>.
/// </summary>
/// <remarks>
/// <para>
/// SCUM stores the game designers' values as tagged properties, so the exact property/member names are the game's, not
/// this tool's. The numeric fields (weight, trader prices, fame) are matched by candidate property names below and are
/// the reliable output; inventory size, rarity, name and description are best-effort (names and localized text vary by
/// build). The command always prints the distinct property names it saw and, with <c>--raw</c>, writes a companion file
/// with every item's full property bag — so an empty field can be mapped correctly from the tool's own first run, by
/// editing the candidate-name lists here, without needing the game files to hand.
/// </para>
/// </remarks>
internal sealed partial class AssetCommands
{
    /// <summary>Item attribute property names, in match order (first readable match wins). The game's names; edit to match a build.</summary>
    private static readonly string[] NameCandidates = ["DisplayName", "ItemName", "Caption", "Name"];
    private static readonly string[] DescriptionCandidates = ["Description", "ItemDescription", "DescriptionText", "FlavorText"];
    private static readonly string[] WeightCandidates = ["Weight", "Mass"];
    private static readonly string[] RarityCandidates = ["_rarity", "Rarity", "ItemRarity", "Tier"];
    private static readonly string[] DlcCandidates = ["DLC", "RequiredDLC", "DlcId", "Dlc"];

    /// <summary>Inventory grid span member names, in match order (whole cells). SCUM: <c>GridInventoryColumnSpan</c>/<c>Row</c>.</summary>
    private static readonly string[] WidthCandidates = ["GridInventoryColumnSpan", "InventoryColumns", "SizeX", "Width"];
    private static readonly string[] HeightCandidates = ["GridInventoryRowSpan", "InventoryRows", "SizeY", "Height"];

    /// <summary>Trade-row member names (in <c>Table_TradeableDesc</c>), in match order. Edit to match a build.</summary>
    private static readonly string[] BuyCandidates = ["BasePurchasePrice", "PurchasePrice", "BasePrice", "BuyPrice", "Price"];
    private static readonly string[] SellCandidates = ["BaseSalePrice", "BaseSellPrice", "SellPrice", "SalePrice"];
    private static readonly string[] FameCandidates = ["RequiredFamePoints", "RequiredFameToBuy", "FamePointsRequired", "FamePoints", "Fame"];

    /// <summary>SCUM stores item weight in grams; SACC's field is kilograms.</summary>
    private const double GramsPerKilogram = 1000.0;

    /// <summary>Path tokens that mark an inventory-size struct; the axis is the final <c>.X</c>/<c>.Y</c> (or a Width/Height name).</summary>
    private static readonly string[] SizeTokens = ["boundingbox", "dimension", "inventorysize", "slotsize", "iconsize", "gridsize"];

    private static Command BuildItems()
    {
        var source = SourceArgument();
        var aes = AesOption();
        var kind = new Option<ModdableKind?>(["--kind", "-k"], "Only this kind (Item, Weapon, Magazine, Attachment, Ammo, Projectile, Vehicle); default = every kind.");
        var output = new Option<string?>(["--out", "-o"], "Output file (default game-item-meta.json).");
        var raw = new Option<bool>("--raw", "Also write <out>.raw.json: every item's full property bag and its trade row, for mapping unknown fields.");
        var command = new Command("items", "Write per-item metadata (name, weight, inventory size, trader prices, fame) to a JSON file keyed by spawn code, for external tools.")
        {
            source, aes, kind, output, raw,
        };
        command.SetHandler((InvocationContext ctx) =>
        {
            var logger = CliHost.CreateLogger<AssetCommands>();
            var parse = ctx.ParseResult;
            var ct = ctx.GetCancellationToken();
            ctx.ExitCode = RunGuarded(logger, () =>
            {
                using var catalog = OpenCatalog(parse.GetValueForArgument(source), parse.GetValueForOption(aes), logger);

                // --- trade numbers: one read of the economy DataTable, grouped by row (the item class, e.g. Weapon_AK47_C).
                var tradeRows = ReadTradeRows(catalog, logger);

                // --- items: every moddable asset of the requested kind(s).
                var filter = parse.GetValueForOption(kind);
                var assets = ModdableAssets.Find(catalog).Where(a => filter is null || a.Kind == filter).ToList();
                if (assets.Count == 0)
                {
                    logger.LogError("No items found in {Source} (is the key right?).", catalog.DisplayName);
                    return 2;
                }

                var items = new SortedDictionary<string, ItemMeta>(StringComparer.OrdinalIgnoreCase);
                var rawItems = parse.GetValueForOption(raw) ? new SortedDictionary<string, RawItem>(StringComparer.OrdinalIgnoreCase) : null;
                var attrNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int withWeight = 0, withSize = 0, withBuy = 0, withFame = 0, withName = 0;
                var done = 0;

                foreach (var asset in assets)
                {
                    ct.ThrowIfCancellationRequested();

                    // Attributes: the item package plus its _ES entity setup (where item data lives), deduped by key.
                    var values = ReadAttributes(catalog, asset, logger);
                    foreach (var t in values)
                    {
                        attrNames.Add(t.Name);
                    }

                    tradeRows.TryGetValue(asset.Name + "_C", out var trade);

                    var meta = new ItemMeta
                    {
                        Code = asset.Name,
                        Name = PickText(values, NameCandidates),
                        Description = PickText(values, DescriptionCandidates),
                        WeightKg = PickNumber(values, WeightCandidates) is { } g ? g / GramsPerKilogram : null,
                        Rarity = PickLabel(values, RarityCandidates),
                        Dlc = PickLabel(values, DlcCandidates),
                        Width = PickSpan(values, WidthCandidates) ?? PickDimension(values, 'X'),
                        Height = PickSpan(values, HeightCandidates) ?? PickDimension(values, 'Y'),
                        TraderBuy = trade is null ? null : PickNumber(trade, BuyCandidates),
                        TraderSell = trade is null ? null : PickNumber(trade, SellCandidates),
                        FameToBuy = trade is null ? null : PickWhole(trade, FameCandidates),
                    };

                    items[asset.Name] = meta;
                    rawItems?.Add(asset.Name, RawItem.From(asset, values, trade));

                    if (meta.WeightKg is not null) withWeight++;
                    if (meta.Width is not null && meta.Height is not null) withSize++;
                    if (meta.TraderBuy is not null) withBuy++;
                    if (meta.FameToBuy is not null) withFame++;
                    if (meta.Name is not null) withName++;

                    if (++done % 500 == 0 || done == assets.Count)
                    {
                        logger.LogInformation("  {Done}/{Total} items.", done, assets.Count);
                    }
                }

                var outPath = parse.GetValueForOption(output) ?? "game-item-meta.json";
                var file = new ItemMetaFile($"SCUM Modding Studio: {catalog.DisplayName}", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), items);
                using (var stream = File.Create(outPath))
                {
                    JsonSerializer.Serialize(stream, file, ItemJson);
                }

                logger.LogInformation("Wrote {Path}: {Count} item(s), {Bytes:N0} bytes.", Path.GetFullPath(outPath), items.Count, new FileInfo(outPath).Length);
                logger.LogInformation("Mapped: {Weight} weight, {Size} inventory size, {Buy} trader price, {Fame} fame, {Name} readable name (of {Total}).",
                    withWeight, withSize, withBuy, withFame, withName, items.Count);
                logger.LogInformation("Trade table: {Rows} row(s); members: {Members}", tradeRows.Count,
                    string.Join(", ", tradeRows.Values.SelectMany(r => r.Select(t => t.Name)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                logger.LogInformation("Item attribute names seen ({Count}): {Names}", attrNames.Count,
                    string.Join(", ", attrNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));

                if (rawItems is not null)
                {
                    var rawPath = Path.ChangeExtension(outPath, null) + ".raw.json";
                    using var stream = File.Create(rawPath);
                    JsonSerializer.Serialize(stream, rawItems, ItemJson);
                    logger.LogInformation("Wrote {Path}: full property bags for mapping.", Path.GetFullPath(rawPath));
                }

                return 0;
            });
        });
        return command;
    }

    /// <summary>The economy table's rows as class-name → members, or empty when the table is not in the source.</summary>
    private static Dictionary<string, List<Tunable>> ReadTradeRows(AssetCatalog catalog, ILogger logger)
    {
        var rows = new Dictionary<string, List<Tunable>>(StringComparer.OrdinalIgnoreCase);
        if (!catalog.PackageExists(DataTableEdits.TradeableTable))
        {
            logger.LogWarning("Economy table {Table} not in source: no trader prices or fame.", DataTableEdits.TradeableTable);
            return rows;
        }

        try
        {
            foreach (var t in AssetEditing.ReadValues(catalog, null, DataTableEdits.TradeableTable))
            {
                // DataTable members read as "Rows[<rowName>].<member…>" (DataTableRows.PathOf).
                if (TradeRowPath().Match(t.Path) is { Success: true } m)
                {
                    (rows.TryGetValue(m.Groups[1].Value, out var list) ? list : rows[m.Groups[1].Value] = []).Add(t);
                }
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or FormatException or InvalidDataException or IOException)
        {
            logger.LogWarning("Could not read {Table}: {Message}", DataTableEdits.TradeableTable, ex.Message);
        }

        return rows;
    }

    /// <summary>An item's tunable values: its own package plus its <c>_ES</c> entity setup, deduped by key (the _ES wins).</summary>
    private static List<Tunable> ReadAttributes(AssetCatalog catalog, ModdableAsset asset, ILogger logger)
    {
        var byKey = new Dictionary<string, Tunable>(StringComparer.Ordinal);
        foreach (var path in new[] { asset.PackagePath, asset.EntitySetupPath })
        {
            if (path is null)
            {
                continue;
            }

            try
            {
                foreach (var t in AssetEditing.ReadValues(catalog, null, path))
                {
                    byKey[t.Key] = t;
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or FormatException or InvalidDataException or IOException)
            {
                logger.LogDebug("Skipped {Path} of {Item}: {Message}", path, asset.Name, ex.Message);
            }
        }

        return byKey.Values.ToList();
    }

    /// <summary>First readable Text value whose property name matches a candidate (localized string-table refs are skipped).</summary>
    private static string? PickText(IEnumerable<Tunable> values, string[] names)
    {
        foreach (var name in names)
        {
            var hit = values.FirstOrDefault(t => t.Kind == TunableKind.Text && t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null && hit.Value.Length > 0 && !(hit.Value.StartsWith('[') && hit.Value.EndsWith(']')))
            {
                return hit.Value;
            }
        }

        return null;
    }

    /// <summary>First numeric value (float/double/int) whose property name matches a candidate.</summary>
    private static double? PickNumber(IEnumerable<Tunable> values, string[] names)
    {
        foreach (var name in names)
        {
            var hit = values.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && t.Kind is TunableKind.Float or TunableKind.Double or TunableKind.Int or TunableKind.UInt);
            if (hit is not null && double.TryParse(hit.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>First whole-number value whose property name matches a candidate.</summary>
    private static long? PickWhole(IEnumerable<Tunable> values, string[] names)
    {
        foreach (var name in names)
        {
            var hit = values.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && t.Kind is TunableKind.Int or TunableKind.UInt);
            if (hit is not null && long.TryParse(hit.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>First enum/text label whose property name matches a candidate (e.g. a rarity enum).</summary>
    private static string? PickLabel(IEnumerable<Tunable> values, string[] names)
    {
        foreach (var name in names)
        {
            var hit = values.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && t.Kind is TunableKind.Enum or TunableKind.Text && t.Value.Length > 0);
            if (hit is not null && !(hit.Value.StartsWith('[') && hit.Value.EndsWith(']')))
            {
                // Enum values read as "EItemRarity::Rare" → keep the readable tail.
                var sep = hit.Value.IndexOf("::", StringComparison.Ordinal);
                return sep >= 0 ? hit.Value[(sep + 2)..] : hit.Value;
            }
        }

        return null;
    }

    /// <summary>First whole inventory-span value (1..999 cells) whose property name matches a candidate (e.g. GridInventoryColumnSpan).</summary>
    private static int? PickSpan(IEnumerable<Tunable> values, string[] names)
    {
        foreach (var name in names)
        {
            var hit = values.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && t.Kind is TunableKind.Int or TunableKind.UInt or TunableKind.Float);
            if (hit is not null && double.TryParse(hit.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1000)
            {
                return (int)Math.Round(v);
            }
        }

        return null;
    }

    /// <summary>An inventory-size axis: a value on a size-struct path ending in the axis, or a Width/Height-named value.</summary>
    private static int? PickDimension(IEnumerable<Tunable> values, char axis)
    {
        var axisName = axis == 'X' ? "Width" : "Height";
        foreach (var t in values)
        {
            if (t.Kind is not (TunableKind.Int or TunableKind.UInt or TunableKind.Float))
            {
                continue;
            }

            var path = t.Path.ToLowerInvariant();
            var onSizeStruct = SizeTokens.Any(tok => path.Contains(tok)) && t.Name.Length == 1 && char.ToUpperInvariant(t.Name[0]) == axis;
            if (onSizeStruct || t.Name.Equals(axisName, StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(t.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1000)
                {
                    return (int)Math.Round(v);
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"^Rows\[(.+?)\]\.(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TradeRowPath();

    private static readonly JsonSerializerOptions ItemJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // The output shape SACC's ItemMetaTable reads: camelCase keys, nulls omitted.
    private sealed record ItemMetaFile(string Source, string ReadAt, IReadOnlyDictionary<string, ItemMeta> Items);

    private sealed record ItemMeta
    {
        public string Code { get; init; } = "";
        public string? Name { get; init; }
        public string? Description { get; init; }
        public double? WeightKg { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? Rarity { get; init; }
        public double? TraderBuy { get; init; }
        public double? TraderSell { get; init; }
        public long? FameToBuy { get; init; }
        public string? Dlc { get; init; }
    }

    // --raw companion: every value, so an unmapped field's real name is visible.
    private sealed record RawItem(string Kind, string Category, string Package, IReadOnlyList<string> Attributes, IReadOnlyList<string> Trade)
    {
        public static RawItem From(ModdableAsset asset, IEnumerable<Tunable> values, IEnumerable<Tunable>? trade) => new(
            asset.Kind.ToString(), asset.Category, asset.PackagePath,
            values.Select(t => $"{t.Path} = {t.Value} ({t.Kind})").OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
            (trade ?? []).Select(t => $"{t.Name} = {t.Value} ({t.Kind})").OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
