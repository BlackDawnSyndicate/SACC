using System.Globalization;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>
/// "Spawn at a map spot" for the GM panel: catalog commands rendered with X Y Z, kits moved
/// to a spot, and a ground height guessed from nearby scum-map markers.
/// </summary>
public static partial class GmSpawn
{
    /// <summary>One GM action: which catalog entry it uses and which of its parameters take the type and count.</summary>
    public sealed record Action(string Key, string Label, string CatalogId, string? TypeParam, string? CountParam, string? FixedType = null);

    public static readonly IReadOnlyList<Action> Actions = new[]
    {
        new Action("animals", "Animals", "spawn-animal", "animal", "amount"),
        new Action("npcs", "NPC squad", "spawn-armed-npc", "npc", "amount"),
        new Action("zombies", "Zombie group", "spawn-zombie", "zombie", "amount"),
        new Action("beepers", "Beepers", "spawn-zombie", "zombie", "amount", FixedType: "BP_Zombie_SuicideVest"),
        new Action("razors", "Razors", "spawn-razor", null, null),
        new Action("cargo", "Cargo drop", "schedule-world-event", null, null),
        new Action("vehicle", "Vehicle", "spawn-vehicle", "vehicle", null),
        new Action("kit", "Kit", "spawn-item", null, null),
    };

    /// <summary>
    /// Chat lines for a catalog action at <paramref name="at"/>. Entries with a count parameter
    /// get it once (large item spawns split later, like every send); entries without one are
    /// repeated <paramref name="count"/> times.
    /// </summary>
    public static TemplateRenderer.Result Render(CommandDef def, Action action, string? type, int count, Vec3 at)
    {
        var values = new Dictionary<string, string?> { ["coords"] = WhalleyBotExport.Coords(at) };
        if (action.TypeParam != null) values[action.TypeParam] = action.FixedType ?? type;
        if (action.CountParam != null) values[action.CountParam] = count.ToString(CultureInfo.InvariantCulture);

        var r = TemplateRenderer.Render(def, values);
        if (!r.Ok) return r;
        if (!r.Text.Contains(values["coords"]!))
            return new TemplateRenderer.Result(r.Text, new[] { $"The '{def.Id}' catalog entry has no {{coords}} parameter, so it can't spawn at a spot." });
        var repeat = action.CountParam == null ? Math.Max(1, count) : 1;
        return new TemplateRenderer.Result(string.Join("\n", Enumerable.Repeat(r.Text, repeat)), r.Errors);
    }

    [GeneratedRegex(@"^#SpawnItem\s", RegexOptions.IgnoreCase)]
    private static partial Regex SpawnItem();

    /// <summary>
    /// Kit lines moved to a spot: every #SpawnItem gets Location "X Y Z" at the end (after any
    /// StackCount, as WhalleyBot's tasks write it). Other lines can't be placed and are counted.
    /// </summary>
    public static List<string> KitAt(IEnumerable<string> kitLines, Vec3 at, out int notPlaced)
    {
        notPlaced = 0;
        var suffix = $" Location \"{WhalleyBotExport.Coords(at)}\"";
        var result = new List<string>();
        foreach (var line in kitLines)
        {
            if (SpawnItem().IsMatch(line)) result.Add(line + suffix);
            else { result.Add(line); notPlaced++; }
        }
        return result;
    }

    /// <summary>Height of the nearest marker that has one, within <paramref name="maxMapUnits"/>; null if none.</summary>
    public static double? NearbyHeight(IEnumerable<MapMarker> markers, double mapX, double mapY, double maxMapUnits) =>
        NearbyHeight(markers, mapX, mapY, maxMapUnits, out _);

    /// <inheritdoc cref="NearbyHeight(IEnumerable{MapMarker}, double, double, double)"/>
    /// <param name="distance">Distance to that marker in map units (infinity when none).</param>
    public static double? NearbyHeight(IEnumerable<MapMarker> markers, double mapX, double mapY, double maxMapUnits, out double distance)
    {
        double best = maxMapUnits * maxMapUnits;
        double? height = null;
        foreach (var m in markers)
        {
            if (m.Height is not { } h) continue;
            var d = (m.MapX - mapX) * (m.MapX - mapX) + (m.MapY - mapY) * (m.MapY - mapY);
            if (d <= best) { best = d; height = h; }
        }
        distance = height == null ? double.PositiveInfinity : Math.Sqrt(best);
        return height;
    }
}
