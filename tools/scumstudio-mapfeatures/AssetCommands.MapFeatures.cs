using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScumStudio.Level.Spawns;

namespace ScumStudio.Cli.Commands;

/// <summary>
/// <c>scumstudio asset map-features &lt;source&gt; -o &lt;file&gt;</c>: reads the island's static spawn data
/// (<see cref="SpawnPlaces.ReadFrom"/>, the one <c>The_Island_LevelStaticData</c> asset) and writes the map overlays
/// external tools draw over a map of the island — currently the threat zones and the hunting areas — as a small JSON file.
/// All coordinates are UE world centimetres (the X/Y of <c>#Location</c>), so a reader needs no projection.
/// </summary>
/// <remarks>
/// <para><b>File format</b> (JSON, version 1):</para>
/// <code>
/// {
///   "version": 1,
///   "huntingAreas": [ { "x": cm, "y": cm, "r": cm, "biome": "BD_Village" }, ... ],
///   "threatZones":  [ { "x": cm, "y": cm, "rx": cm, "ry": cm, "yaw": deg, "settings": "MTZ_Settlement_Large" }, ... ]
/// }
/// </code>
/// <para>Hunting areas are circles (centre + radius); threat zones are ellipses (centre + semi-axes + yaw). The
/// <c>biome</c>/<c>settings</c> are the leaf names of the game's population/zone settings assets.</para>
/// </remarks>
internal sealed partial class AssetCommands
{
    private static Command BuildMapFeatures()
    {
        var source = SourceArgument();
        var aes = AesOption();
        var output = new Option<string?>(["--out", "-o"], "Output file (default map-features.json).");
        var command = new Command("map-features", "Write the island's map overlays (threat zones, hunting areas) to a JSON file for external tools.")
        {
            source, aes, output,
        };
        command.SetHandler((InvocationContext ctx) =>
        {
            var logger = CliHost.CreateLogger<AssetCommands>();
            var parse = ctx.ParseResult;
            ctx.ExitCode = RunGuarded(logger, () =>
            {
                using var catalog = OpenCatalog(parse.GetValueForArgument(source), parse.GetValueForOption(aes), logger);
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

                var features = new MapFeaturesFile(1, hunting, zones);
                var path = parse.GetValueForOption(output) ?? "map-features.json";
                using (var stream = File.Create(path))
                {
                    JsonSerializer.Serialize(stream, features, MapFeaturesJson);
                }

                logger.LogInformation("Wrote {Path}: {Zones} threat zone(s), {Hunting} hunting area(s), {Bytes:N0} bytes.",
                    Path.GetFullPath(path), zones.Count, hunting.Count, new FileInfo(path).Length);
                // The distinct settings/biome names, so the tier (High/Medium/Low) mapping can be made from the real data.
                logger.LogInformation("Zone settings: {Settings}", string.Join(", ", zones.Select(z => z.Settings).Distinct().OrderBy(s => s)));
                logger.LogInformation("Hunting biomes: {Biomes}", string.Join(", ", hunting.Select(h => h.Biome).Distinct().OrderBy(s => s)));
                return 0;
            });
        });
        return command;
    }

    private static readonly JsonSerializerOptions MapFeaturesJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record MapFeaturesFile(int Version, IReadOnlyList<HuntingArea> HuntingAreas, IReadOnlyList<ThreatZone> ThreatZones);

    private sealed record HuntingArea(float X, float Y, float R, string Biome);

    private sealed record ThreatZone(float X, float Y, float Rx, float Ry, float Yaw, string Settings);
}
