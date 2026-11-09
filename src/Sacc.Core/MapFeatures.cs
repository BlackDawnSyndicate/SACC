using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sacc.Core;

/// <summary>SCUM's threat tier for a zone, read from its settings prefix (HTZ/MTZ/LTZ).</summary>
public enum ThreatTier
{
    /// <summary>No threat settings (a bare zone), or an unrecognised prefix.</summary>
    None,
    Low,
    Medium,
    High,
}

/// <summary>A hunting area: a circle (world cm) where animals of a biome live.</summary>
public sealed record HuntingArea(double X, double Y, double R, string Biome)
{
    /// <summary>True for a mutant den (BD_MutantDen_*), which is dangerous rather than game to hunt.</summary>
    public bool IsMutantDen => Biome.Contains("MutantDen", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A threat zone: an ellipse (world cm, semi-axes) that spawns zombies/NPCs; its tier comes from the settings name.</summary>
public sealed record ThreatZone(double X, double Y, double Rx, double Ry, double Yaw, string Settings)
{
    /// <summary>The zone's threat tier from its settings prefix (HTZ_ = High, MTZ_ = Medium, LTZ_ = Low).</summary>
    public ThreatTier Tier => Settings switch
    {
        _ when Settings.StartsWith("HTZ", StringComparison.OrdinalIgnoreCase) => ThreatTier.High,
        _ when Settings.StartsWith("MTZ", StringComparison.OrdinalIgnoreCase) => ThreatTier.Medium,
        _ when Settings.StartsWith("LTZ", StringComparison.OrdinalIgnoreCase) => ThreatTier.Low,
        _ => ThreatTier.None,
    };
}

/// <summary>
/// The island's map overlays, produced by SCUM Modding Studio's <c>asset map-features</c> (see the tool in
/// <c>tools/scumstudio-mapfeatures/</c>): the threat zones and hunting areas, in UE world centimetres (the same X/Y the
/// map uses), read from a small JSON file. SACC draws them over the Map tab; it holds no game data itself.
/// </summary>
public sealed class MapFeatures
{
    public MapFeatures(IReadOnlyList<HuntingArea> huntingAreas, IReadOnlyList<ThreatZone> threatZones)
    {
        HuntingAreas = huntingAreas;
        ThreatZones = threatZones;
    }

    public IReadOnlyList<HuntingArea> HuntingAreas { get; }
    public IReadOnlyList<ThreatZone> ThreatZones { get; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads a map-features JSON file.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="InvalidDataException">The file is not valid map-features JSON.</exception>
    public static MapFeatures Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads map-features JSON from a stream.</summary>
    /// <exception cref="InvalidDataException">The content is not valid map-features JSON.</exception>
    public static MapFeatures Read(Stream stream)
    {
        FileDto file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(stream, Options)
                   ?? throw new InvalidDataException("Empty map-features file.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Not valid map-features JSON: " + ex.Message, ex);
        }

        var hunting = (file.HuntingAreas ?? [])
            .Select(h => new HuntingArea(h.X, h.Y, h.R, h.Biome ?? ""))
            .ToList();
        var zones = (file.ThreatZones ?? [])
            .Select(z => new ThreatZone(z.X, z.Y, z.Rx, z.Ry, z.Yaw, z.Settings ?? ""))
            .ToList();
        return new MapFeatures(hunting, zones);
    }

    // The on-disk shape (camelCase; case-insensitive matching handles either casing).
    private sealed class FileDto
    {
        public int Version { get; set; }
        public List<HuntingDto>? HuntingAreas { get; set; }
        public List<ZoneDto>? ThreatZones { get; set; }
    }

    private sealed class HuntingDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double R { get; set; }
        public string? Biome { get; set; }
    }

    private sealed class ZoneDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Rx { get; set; }
        public double Ry { get; set; }
        public double Yaw { get; set; }
        public string? Settings { get; set; }
    }
}
