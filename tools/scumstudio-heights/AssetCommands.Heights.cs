using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ScumStudio.Assets.Catalog;
using ScumStudio.Assets.Landscape;
using ScumStudio.Formats.Packages;
using ScumStudio.Level.World;

namespace ScumStudio.Cli.Commands;

/// <summary>
/// <c>scumstudio asset heights &lt;source&gt; [tile...] --step &lt;cm&gt; -o &lt;file&gt;</c>: samples the landscape's ground height
/// (<see cref="LandscapeExtractor"/> / <see cref="LandscapeSurface.SampleHeight"/>) on a regular world grid and writes it as a
/// small binary heightfield (<c>.scgh</c>) that external tools read without the game files. No tiles = the whole island.
/// </summary>
/// <remarks>
/// <para><b>Grid file format</b> (little-endian), version 1:</para>
/// <list type="table">
/// <item><term>0</term><description>char[4] magic "SCGH"</description></item>
/// <item><term>4</term><description>u16 version (1)</description></item>
/// <item><term>6</term><description>u16 flags (0)</description></item>
/// <item><term>8</term><description>f32 cellSizeCm (world cm between columns and rows)</description></item>
/// <item><term>12</term><description>f32 originXcm (world X of column 0's sample centre)</description></item>
/// <item><term>16</term><description>f32 originYcm (world Y of row 0's sample centre)</description></item>
/// <item><term>20</term><description>u32 width (columns, X)</description></item>
/// <item><term>24</term><description>u32 height (rows, Y)</description></item>
/// <item><term>28</term><description>f32 zMinCm, 32: f32 zMaxCm (height decode range)</description></item>
/// <item><term>36</term><description>u16 nodata (0xFFFF), 38: u16 reserved</description></item>
/// <item><term>40</term><description>u16[width*height] row-major (index y*width+x); 0xFFFF = no terrain,
/// else zCm = zMin + raw * (zMax - zMin) / 65534</description></item>
/// </list>
/// <para>World cm are UE world coordinates: the same X/Y that <c>#Location</c> and <c>#SpawnItem … Location</c> use.</para>
/// </remarks>
internal sealed partial class AssetCommands
{
    private const ushort HeightsNoData = 0xFFFF;
    private const int HeightsMaxRaw = 0xFFFE;

    private static Command BuildHeights()
    {
        var source = SourceArgument();
        var tiles = new Argument<string[]>("tile", "Landscape tiles (e.g. Landscape_A_0_4d) or cell names (A_0); none = the whole island.")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };
        var aes = AesOption();
        var step = new Option<double>("--step", () => 400.0, "Grid cell size in world centimetres (default 400 = 4 m).");
        var output = new Option<string?>(["--out", "-o"], "Output file (default heights.scgh).");
        var command = new Command("heights", "Write the landscape ground height on a regular world grid to a binary .scgh file for external tools.")
        {
            source, tiles, aes, step, output,
        };
        command.SetHandler((InvocationContext ctx) =>
        {
            var logger = CliHost.CreateLogger<AssetCommands>();
            var parse = ctx.ParseResult;
            var ct = ctx.GetCancellationToken();
            ctx.ExitCode = RunGuarded(logger, () =>
            {
                var cell = parse.GetValueForOption(step);
                if (!(cell >= 1.0))
                {
                    logger.LogError("--step must be at least 1 cm.");
                    return 2;
                }

                using var catalog = OpenCatalog(parse.GetValueForArgument(source), parse.GetValueForOption(aes), logger);
                var names = parse.GetValueForArgument(tiles);
                var packages = names.Length == 0 ? AllLandscapeTiles(catalog) : ResolveLandscapeTiles(catalog, names, logger);
                if (packages is null)
                {
                    return 2;
                }

                if (packages.Count == 0)
                {
                    logger.LogError("No landscape tiles found in {Source}.", catalog.DisplayName);
                    return 2;
                }

                // Pass 1: the grid's world bounds from the tiles' headers (cheap; no landscape decode yet).
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                var bounded = 0;
                foreach (var path in packages)
                {
                    if (TileBounds(catalog, path) is not { } b)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, b.MinX);
                    minY = Math.Min(minY, b.MinY);
                    maxX = Math.Max(maxX, b.MaxX);
                    maxY = Math.Max(maxY, b.MaxY);
                    bounded++;
                }

                if (bounded == 0)
                {
                    logger.LogError("None of the {Count} tile(s) had readable world bounds.", packages.Count);
                    return 2;
                }

                var width = (int)Math.Floor((maxX - minX) / cell) + 1;
                var height = (int)Math.Floor((maxY - minY) / cell) + 1;
                long count = (long)width * height;
                if (count > 250_000_000)
                {
                    logger.LogError("Grid too large: {W}x{H} = {N:N0} cells. Use a bigger --step.", width, height, count);
                    return 2;
                }

                logger.LogInformation("{Tiles} tile(s), grid {W}x{H} ({N:N0} cells) at {Step:0} cm over X [{MinX:0}..{MaxX:0}] Y [{MinY:0}..{MaxY:0}].",
                    packages.Count, width, height, count, cell, minX, maxX, minY, maxY);

                var heights = new float[count];
                Array.Fill(heights, float.NaN);

                // Pass 2: decode each tile's landscape and sample it onto the grid cells it covers.
                var clock = Stopwatch.StartNew();
                var options = new LandscapeExtractOptions { Step = 1, ComputeNormals = false, PackedNormals = false, KeepSurface = true, ReadLayers = false };
                var filled = 0L;
                var done = 0;
                foreach (var path in packages)
                {
                    ct.ThrowIfCancellationRequested();
                    IReadOnlyList<LandscapeProxyMesh> proxies;
                    try
                    {
                        proxies = LandscapeExtractor.Extract(catalog, path, options);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                    {
                        logger.LogWarning("Skipped {Tile}: {Message}", path, ex.Message);
                        continue;
                    }

                    foreach (var surface in proxies.SelectMany(p => p.Components).Select(c => c.Surface).OfType<LandscapeSurface>())
                    {
                        var cx0 = Math.Clamp((int)Math.Floor((surface.WorldMinXY.X - minX) / cell), 0, width - 1);
                        var cx1 = Math.Clamp((int)Math.Ceiling((surface.WorldMaxXY.X - minX) / cell), 0, width - 1);
                        var cy0 = Math.Clamp((int)Math.Floor((surface.WorldMinXY.Y - minY) / cell), 0, height - 1);
                        var cy1 = Math.Clamp((int)Math.Ceiling((surface.WorldMaxXY.Y - minY) / cell), 0, height - 1);
                        for (var cy = cy0; cy <= cy1; cy++)
                        {
                            var wy = (float)(minY + cy * cell);
                            for (var cx = cx0; cx <= cx1; cx++)
                            {
                                var index = (long)cy * width + cx;
                                if (!float.IsNaN(heights[index]))
                                {
                                    continue; // a neighbouring component already set this shared cell
                                }

                                var wx = (float)(minX + cx * cell);
                                if (surface.TryGetQuadCoordinates(wx, wy, out var qx, out var qy))
                                {
                                    heights[index] = surface.SampleHeight(qx, qy);
                                    filled++;
                                }
                            }
                        }
                    }

                    if (++done % 16 == 0 || done == packages.Count)
                    {
                        logger.LogInformation("  {Done}/{Total} tiles, {Filled:N0} cells with ground ({Ms:0} ms).", done, packages.Count, filled, clock.Elapsed.TotalMilliseconds);
                    }
                }

                if (filled == 0)
                {
                    logger.LogError("No cell had terrain: nothing written.");
                    return 2;
                }

                var path2 = parse.GetValueForOption(output) ?? "heights.scgh";
                WriteGrid(path2, width, height, cell, minX, minY, heights);
                var fraction = (double)filled / count;
                logger.LogInformation("Wrote {Path}: {W}x{H}, {Filled:N0} cells with ground ({Pct:0.0}%), {Bytes:N0} bytes.",
                    Path.GetFullPath(path2), width, height, filled, fraction * 100, 40L + count * 2);
                return 0;
            });
        });
        return command;
    }

    /// <summary>Every landscape map tile of the island, by package path.</summary>
    private static List<string> AllLandscapeTiles(AssetCatalog catalog) => WorldIndex.FromCatalog(catalog).Packages
        .Where(p => p.Kind == WorldPackageKind.Landscape && p.IsMap)
        .Select(p => p.PackagePath)
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>World X/Y bounds (cm) of a landscape tile from its World Composition header, or null when unreadable.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? TileBounds(AssetCatalog catalog, string path)
    {
        try
        {
            if (!catalog.TryGetPackageFile(path, out var file))
            {
                return null;
            }

            var tile = WorldTileInfo.TryRead(CookedPackage.Parse(file.Read(), [], null, path));
            return tile is { BoundsValid: true } ? (tile.BoundsMin.X, tile.BoundsMin.Y, tile.BoundsMax.X, tile.BoundsMax.Y) : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Encodes a height grid to the <c>.scgh</c> format (see the class remarks) and writes it.</summary>
    internal static void WriteGrid(string path, int width, int height, double cellSizeCm, double originXcm, double originYcm, float[] heightsCm)
    {
        float zmin = float.MaxValue, zmax = float.MinValue;
        foreach (var z in heightsCm)
        {
            if (float.IsNaN(z))
            {
                continue;
            }

            zmin = Math.Min(zmin, z);
            zmax = Math.Max(zmax, z);
        }

        var span = zmax - zmin;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(new[] { (byte)'S', (byte)'C', (byte)'G', (byte)'H' });
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((float)cellSizeCm);
        writer.Write((float)originXcm);
        writer.Write((float)originYcm);
        writer.Write(width);
        writer.Write(height);
        writer.Write(zmin);
        writer.Write(zmax);
        writer.Write(HeightsNoData);
        writer.Write((ushort)0);
        foreach (var z in heightsCm)
        {
            if (float.IsNaN(z) || span <= 0f)
            {
                writer.Write(float.IsNaN(z) ? HeightsNoData : (ushort)0);
                continue;
            }

            var raw = (int)Math.Round((z - zmin) / span * HeightsMaxRaw, MidpointRounding.AwayFromZero);
            writer.Write((ushort)Math.Clamp(raw, 0, HeightsMaxRaw));
        }
    }
}
