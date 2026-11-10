using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ScumStudio.Level.Model;
using ScumStudio.Level.Reading;
using ScumStudio.Level.Spawns;
using ScumStudio.Level.World;

namespace ScumStudio.Cli.Commands;

/// <summary>
/// <c>scumstudio asset map-features &lt;source&gt; -o &lt;file&gt;</c>: reads the island's static data and writes the map overlays
/// external tools draw over a map of the island, as a small JSON file. Threat zones and hunting areas come from the one
/// <c>The_Island_LevelStaticData</c> asset (cheap); location names come from the POI sublevel index (cheap); mech patrol
/// paths need the POI sublevels' actors (heavier — pass <c>--no-paths</c> to skip them). Coordinates are UE world
/// centimetres (the X/Y of <c>#Location</c>), so a reader needs no projection.
/// </summary>
internal sealed partial class AssetCommands
{
    private static Command BuildMapFeatures()
    {
        var source = SourceArgument();
        var aes = AesOption();
        var output = new Option<string?>(["--out", "-o"], "Output file (default map-features.json).");
        var noPaths = new Option<bool>("--no-paths", "Skip the mech patrol paths (which read the POI sublevels' actors and take longer).");
        var command = new Command("map-features", "Write the island's map overlays (threat zones, hunting areas, locations, patrol paths) to a JSON file for external tools.")
        {
            source, aes, output, noPaths,
        };
        command.SetHandler((InvocationContext ctx) =>
        {
            var logger = CliHost.CreateLogger<AssetCommands>();
            var parse = ctx.ParseResult;
            var ct = ctx.GetCancellationToken();
            ctx.ExitCode = RunGuarded(logger, () =>
            {
                using var catalog = OpenCatalog(parse.GetValueForArgument(source), parse.GetValueForOption(aes), logger);

                // --- threat zones + hunting areas: the one static-data asset.
                var places = SpawnPlaces.ReadFrom(catalog);
                if (places.Count == 0)
                {
                    logger.LogError("No spawn places found: {Asset} is not in {Source} (is the key right?).", SpawnPlaces.StaticDataPath, catalog.DisplayName);
                    return 2;
                }

                var hunting = places
                    .Where(p => p.Kind == SpawnPlaceKind.Animal)
                    .Select(p => new HuntingArea(p.Transform.Translation.X, p.Transform.Translation.Y, p.SizeX, SpawnPlaces.SettingName(p.Group)))
                    .ToList();
                var zones = places
                    .Where(p => p.Kind == SpawnPlaceKind.Zone)
                    .Select(p => new ThreatZone(p.Transform.Translation.X, p.Transform.Translation.Y, p.SizeX, p.SizeY,
                        p.Transform.Rotation.Rotator().Yaw, SpawnPlaces.SettingName(p.Group)))
                    .ToList();

                // --- locations: the POI sublevels' names at their tile centres (header reads only, ~1s).
                var index = WorldIndex.FromCatalog(catalog).WithTileInfo(catalog, logger, cancellationToken: ct);
                var locations = index.OfKind(WorldPackageKind.Poi)
                    .Where(p => p.Tile is { BoundsValid: true })
                    .Select(p => (Name: LocationName(p.Name), Cell: p.Cell, Cx: (p.Tile!.BoundsMin.X + p.Tile!.BoundsMax.X) / 2f, Cy: (p.Tile!.BoundsMin.Y + p.Tile!.BoundsMax.Y) / 2f))
                    .Where(x => x.Name.Length > 0)
                    .GroupBy(x => (x.Cell, x.Name))
                    .Select(g => new Location((float)g.Average(x => x.Cx), (float)g.Average(x => x.Cy), g.Key.Name))
                    .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // --- patrol paths: each SentrySpawner2's route (its stand + patrol points), from the POI/bunker sublevels' actors.
                var paths = new List<PatrolPath>();
                if (!parse.GetValueForOption(noPaths))
                {
                    var reader = new Cue4ParseLevelReader(catalog, new Cue4ParseLevelReaderOptions(), logger);
                    var sublevels = index.Packages.Where(p => p.Kind is WorldPackageKind.Poi or WorldPackageKind.TvBase).Select(p => p.PackagePath).ToList();
                    logger.LogInformation("Reading {Count} POI/bunker sublevel(s) for patrol paths…", sublevels.Count);
                    var clock = Stopwatch.StartNew();
                    var done = 0;
                    foreach (var path in sublevels)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            foreach (var actor in LevelDocument.Load(reader, path, ct).Actors.Where(a => a.ClassName == "SentrySpawner2" && a.PatrolPoints.Count > 0))
                            {
                                var pts = new List<double[]> { new[] { (double)actor.WorldTransform.Translation.X, actor.WorldTransform.Translation.Y } };
                                pts.AddRange(actor.PatrolPoints.Select(p => actor.WorldTransform.TransformPosition(p)).Select(w => new[] { (double)w.X, w.Y }));
                                paths.Add(new PatrolPath(pts));
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                        {
                            logger.LogDebug("Skipped {Level}: {Message}", path, ex.Message);
                        }

                        if (++done % 200 == 0 || done == sublevels.Count)
                        {
                            logger.LogInformation("  {Done}/{Total} sublevels, {Paths} patrol path(s) ({Ms:0} ms).", done, sublevels.Count, paths.Count, clock.Elapsed.TotalMilliseconds);
                        }
                    }
                }

                var features = new MapFeaturesFile(1, hunting, zones, locations, paths);
                var outPath = parse.GetValueForOption(output) ?? "map-features.json";
                using (var stream = File.Create(outPath))
                {
                    JsonSerializer.Serialize(stream, features, MapFeaturesJson);
                }

                logger.LogInformation("Wrote {Path}: {Zones} threat zone(s), {Hunting} hunting area(s), {Locations} location(s), {Paths} patrol path(s), {Bytes:N0} bytes.",
                    Path.GetFullPath(outPath), zones.Count, hunting.Count, locations.Count, paths.Count, new FileInfo(outPath).Length);
                logger.LogInformation("Zone settings: {Settings}", string.Join(", ", zones.Select(z => z.Settings).Distinct().OrderBy(s => s)));
                logger.LogInformation("Hunting biomes: {Biomes}", string.Join(", ", hunting.Select(h => h.Biome).Distinct().OrderBy(s => s)));
                logger.LogInformation("Locations: {Names}", string.Join(", ", locations.Select(l => l.Name).Distinct().OrderBy(s => s)));
                return 0;
            });
        });
        return command;
    }

    private static readonly HashSet<string> LocationCutWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Exterior", "Interior", "Ext", "Int", "BuiltData", "HLOD", "Surroundings", "Block",
        "Atmosphere", "Spawners", "ProceduralFoliageBlockers", "RainEmitters", "Halloween", "HalloweenEvent",
    };

    /// <summary>
    /// A POI sublevel's base place name: drop the cell prefix (A_0_), then keep the leading words up to the first number or
    /// structural word, so "A_0_Samobor_02_PoliceStation" and "A_0_Samobor_05_Block" both collapse to "Samobor".
    /// </summary>
    private static string LocationName(string sublevelName)
    {
        var s = CellPrefix().Replace(sublevelName, "").Replace('_', ' ');
        var name = new List<string>();
        foreach (var token in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (char.IsDigit(token[0]) || LocationCutWords.Contains(token))
            {
                break;
            }

            name.Add(token);
        }

        return string.Join(' ', name).Trim();
    }

    [GeneratedRegex(@"^[ABCDZ]_[0-4]_", RegexOptions.CultureInvariant)]
    private static partial Regex CellPrefix();

    private static readonly JsonSerializerOptions MapFeaturesJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record MapFeaturesFile(int Version, IReadOnlyList<HuntingArea> HuntingAreas, IReadOnlyList<ThreatZone> ThreatZones,
        IReadOnlyList<Location> Locations, IReadOnlyList<PatrolPath> PatrolPaths);

    private sealed record HuntingArea(float X, float Y, float R, string Biome);

    private sealed record ThreatZone(float X, float Y, float Rx, float Ry, float Yaw, string Settings);

    private sealed record Location(float X, float Y, string Name);

    private sealed record PatrolPath(IReadOnlyList<double[]> Points);
}
