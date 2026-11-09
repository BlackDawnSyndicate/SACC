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
    /// <summary>
    /// One GM action: which catalog entry it uses and which of its parameters take the type and count.
    /// <paramref name="GroundChecked"/>: the game refuses this spawn ("Cannot spawn at the specified
    /// location.") when no ground is near the given Z, so SACC can climb to the surface (see <see cref="ClimbHeights"/>).
    /// </summary>
    public sealed record Action(string Key, string Label, string CatalogId, string? TypeParam, string? CountParam, string? FixedType = null,
                                bool GroundChecked = false);

    // Ground-checked (spikes/npc-spawn-ground): #SpawnArmedNPC refuses a spot 10 m underground. #SpawnZombie is
    // refused too, once per zombie (seen in SCUM's log 2026-11-01), though its window may differ: the spike's one
    // zombie 10 m underground was accepted. #SpawnAnimal and #SpawnRazor were accepted there and are untested further.
    public static readonly IReadOnlyList<Action> Actions = new[]
    {
        new Action("animals", "Animals", "spawn-animal", "animal", "amount"),
        new Action("npcs", "NPC squad", "spawn-armed-npc", "npc", "amount", GroundChecked: true),
        new Action("zombies", "Zombie group", "spawn-zombie", "zombie", "amount", GroundChecked: true),
        new Action("beepers", "Beepers", "spawn-zombie", "zombie", "amount", FixedType: "BP_Zombie_SuicideVest", GroundChecked: true),
        new Action("razors", "Razors", "spawn-razor", null, null),
        new Action("cargo", "Cargo drop", "schedule-world-event", null, null),
        new Action("vehicle", "Vehicle", "spawn-vehicle", "vehicle", null),
        new Action("kit", "Kit", "spawn-item", null, null),
    };

    /// <summary>The #SpawnAnimal blueprints the game's own chat autocomplete offers (read off it in game, 2026-11-01).</summary>
    public static readonly IReadOnlyList<string> Animals = new[]
    {
        "BP_Bear", "BP_Boar", "BP_Chicken", "BP_Deer", "BP_Donkey", "BP_Goat", "BP_Horse", "BP_Rabbit", "BP_Wolf",
        "BP_Bear_Mutant", "BP_Boar_Mutant", "BP_Deer_Mutant", "BP_Wolf_Mutant", "BP_BTBear_Mutant_ApexHunt",
    };

    /// <summary>Older animal blueprint names (SAH's table) the game no longer accepts, and their current names.</summary>
    private static readonly Dictionary<string, string> RenamedAnimals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BP_Bear2"] = "BP_Bear", ["BP_Deer2"] = "BP_Deer", ["BP_Donkey2"] = "BP_Donkey", ["BP_Goat2"] = "BP_Goat",
        ["BP_Horse2"] = "BP_Horse", ["BP_Rabbit2"] = "BP_Rabbit", ["BP_Wolf3"] = "BP_Wolf",
    };

    /// <summary>The current name for an animal blueprint, or the name unchanged.</summary>
    public static string CurrentAnimal(string name) => RenamedAnimals.GetValueOrDefault(name.Trim(), name);

    /// <summary>Fixes the animal in a "#SpawnAnimal BP_x [n]" line; anything else is returned unchanged.</summary>
    public static string FixAnimalCommand(string line)
    {
        var m = SpawnAnimalLine().Match(line);
        return m.Success ? m.Groups[1].Value + CurrentAnimal(m.Groups[2].Value) + m.Groups[3].Value : line;
    }

    [GeneratedRegex(@"^(\s*#SpawnAnimal\s+)(\S+)(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SpawnAnimalLine();

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

    /// <summary>What the game says, once per NPC or zombie, when there's no ground near the requested Z. A successful spawn says nothing.</summary>
    public const string RefusalText = "Cannot spawn at the specified location.";

    public static int CountRefusals(string? logText)
    {
        if (string.IsNullOrEmpty(logText)) return 0;
        int n = 0, i = 0;
        while ((i = logText.IndexOf(RefusalText, i, StringComparison.Ordinal)) >= 0) { n++; i += RefusalText.Length; }
        return n;
    }

    /// <summary>
    /// Heights to try, lowest first. The game accepts a ground-checked spawn when the ground is from about
    /// 3 m above to 20-30 m below Z, never spawns it underground, and spawns it in mid-air when Z is high.
    /// Climbing up from below the lowest likely ground means every step under the surface is refused (free),
    /// and the first accepted step is within one <paramref name="step"/> of it, so the spawn lands on the ground.
    /// </summary>
    public static List<double> ClimbHeights(double guess, double below, double above, double step)
    {
        step = Math.Max(50, Math.Abs(step));
        var heights = new List<double>();
        for (var z = guess - Math.Abs(below); z <= guess + Math.Abs(above) + 0.001; z += step) heights.Add(Math.Round(z, 2));
        return heights;
    }

    /// <summary>
    /// The whole climb, in sending order: guess ± range first; if every step of that is refused, the ground
    /// is further off, so carry on upwards to guess + 4×range (still from below, still lands on the ground),
    /// then climb guess − 4×range … guess − range. That last stretch is the only one that can start above the
    /// ground (if it's even lower), so it comes last.
    /// With <paramref name="quickBelow"/> &gt; 0 (a guess from measured ground), a short climb from that many steps
    /// below the guess to one step above goes first; it usually lands within a few steps instead of ten.
    /// If all of it is refused, the full climb follows.
    /// </summary>
    public static List<double> ClimbPlan(double guess, double range, double step, int quickBelow = 0)
    {
        range = Math.Abs(range);
        step = Math.Max(50, Math.Abs(step));
        var plan = quickBelow > 0 ? ClimbHeights(guess, quickBelow * step, step, step) : new List<double>();
        plan.AddRange(ClimbHeights(guess, range, range, step));
        plan.AddRange(ClimbHeights(guess + range + step, 0, 3 * range - step, step));
        plan.AddRange(ClimbHeights(guess - 4 * range, 0, 3 * range - step, step));
        return plan;
    }

    /// <summary>
    /// After a spotter climb stopped at <paramref name="spawnedAt"/>: the height the gear goes, or null when the
    /// climb didn't show the ground. It does only when the step right before was refused and lower, since the
    /// climb plan jumps between stretches.
    /// </summary>
    public static double? SpotterGround(IReadOnlyList<double> heights, IReadOnlyList<bool> refused, int spawnedAt) =>
        spawnedAt > 0 && spawnedAt < heights.Count && refused[spawnedAt - 1] && heights[spawnedAt - 1] < heights[spawnedAt]
            ? heights[spawnedAt]
            : null;

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

    /// <summary>
    /// A rough ground height anywhere: inverse-distance weighted average of the <paramref name="k"/> nearest
    /// markers that have a height, at any distance. Measured against six known ground points it was within
    /// 1-8 m where markers are dense. <paramref name="farthest"/> is the distance to the k-th marker in map units.
    /// </summary>
    public static double? EstimateHeight(IEnumerable<MapMarker> markers, double mapX, double mapY, out double farthest, int k = 6)
    {
        var nearest = markers.Where(m => m.Height != null)
            .Select(m => (D: Math.Sqrt((m.MapX - mapX) * (m.MapX - mapX) + (m.MapY - mapY) * (m.MapY - mapY)), H: m.Height!.Value))
            .OrderBy(t => t.D).Take(k).ToList();
        farthest = nearest.Count == 0 ? double.PositiveInfinity : nearest[^1].D;
        if (nearest.Count == 0) return null;
        // Weights stop growing within 0.01 map units (about 50 cm) so a marker right on the spot doesn't divide by zero.
        double sum = 0, weights = 0;
        foreach (var (d, h) in nearest) { var w = 1 / Math.Pow(Math.Max(d, 0.01), 2); sum += w * h; weights += w; }
        return sum / weights;
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
