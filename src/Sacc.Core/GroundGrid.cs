using System.Text;

namespace Sacc.Core;

/// <summary>
/// A regular grid of landscape ground heights over the map, read from a <c>.scgh</c> file produced by SCUM Modding
/// Studio's <c>scumstudio asset heights</c> command (which decodes the game's own landscape tiles). World X/Y are UE
/// world centimetres — the same coordinates <c>#Location</c> and <c>#SpawnItem … Location</c> use — so a height is a
/// direct lookup with no projection. This is the most accurate ground source when a grid is loaded.
/// </summary>
/// <remarks>
/// Format (little-endian), version 1: "SCGH", u16 version, u16 flags, f32 cellSizeCm, f32 originXcm, f32 originYcm,
/// u32 width, u32 height, f32 zMinCm, f32 zMaxCm, u16 nodata (0xFFFF), u16 reserved, then width*height u16 row-major
/// (index y*width+x); 0xFFFF = no terrain, else zCm = zMin + raw*(zMax-zMin)/65534.
/// </remarks>
public sealed class GroundGrid
{
    public const ushort NoData = 0xFFFF;
    public const int MaxRaw = 0xFFFE;
    private static readonly byte[] Magic = "SCGH"u8.ToArray();

    private readonly ushort[] _raw;
    private readonly float _zMin;
    private readonly float _zSpan;

    private GroundGrid(float cellSizeCm, float originXcm, float originYcm, int width, int height, float zMin, float zMax, ushort[] raw)
    {
        CellSizeCm = cellSizeCm;
        OriginXCm = originXcm;
        OriginYCm = originYcm;
        Width = width;
        Height = height;
        _zMin = zMin;
        _zSpan = zMax - zMin;
        _raw = raw;
    }

    /// <summary>World cm between columns and rows.</summary>
    public float CellSizeCm { get; }

    /// <summary>World X of column 0's sample.</summary>
    public float OriginXCm { get; }

    /// <summary>World Y of row 0's sample.</summary>
    public float OriginYCm { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Cells that have a terrain height.</summary>
    public int FilledCells
    {
        get
        {
            var n = 0;
            foreach (var r in _raw)
            {
                if (r != NoData)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The ground height (world cm) at world (<paramref name="x"/>, <paramref name="y"/>) from the nearest cell, or null off the grid or where there is no terrain.</summary>
    public double? At(double x, double y)
    {
        var cx = (int)Math.Round((x - OriginXCm) / CellSizeCm, MidpointRounding.AwayFromZero);
        var cy = (int)Math.Round((y - OriginYCm) / CellSizeCm, MidpointRounding.AwayFromZero);
        if (cx < 0 || cy < 0 || cx >= Width || cy >= Height)
        {
            return null;
        }

        var raw = _raw[(long)cy * Width + cx];
        return raw == NoData ? null : _zMin + raw * (double)_zSpan / MaxRaw;
    }

    /// <summary>Reads a grid file.</summary>
    public static GroundGrid Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a grid from a stream.</summary>
    public static GroundGrid Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
        {
            throw new InvalidDataException("Not a SCGH height grid (bad magic).");
        }

        var version = reader.ReadUInt16();
        if (version != 1)
        {
            throw new InvalidDataException($"Unsupported SCGH version {version}.");
        }

        reader.ReadUInt16(); // flags
        var cell = reader.ReadSingle();
        var originX = reader.ReadSingle();
        var originY = reader.ReadSingle();
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var zMin = reader.ReadSingle();
        var zMax = reader.ReadSingle();
        reader.ReadUInt16(); // nodata (0xFFFF by definition)
        reader.ReadUInt16(); // reserved
        if (width <= 0 || height <= 0 || (long)width * height > 500_000_000 || !(cell >= 1f))
        {
            throw new InvalidDataException($"Implausible SCGH header: {width}x{height}, cell {cell}.");
        }

        var count = (long)width * height;
        var raw = new ushort[count];
        for (long i = 0; i < count; i++)
        {
            raw[i] = reader.ReadUInt16();
        }

        return new GroundGrid(cell, originX, originY, width, height, zMin, zMax, raw);
    }

    /// <summary>Writes a grid in the <c>.scgh</c> format. <paramref name="heightsCm"/> is row-major (y*width+x); NaN = no terrain.</summary>
    public static void Write(Stream stream, int width, int height, double cellSizeCm, double originXcm, double originYcm, IReadOnlyList<float> heightsCm)
    {
        ArgumentNullException.ThrowIfNull(heightsCm);
        if (width <= 0 || height <= 0 || heightsCm.Count != (long)width * height)
        {
            throw new ArgumentException("heightsCm must have width*height values.");
        }

        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var z in heightsCm)
        {
            if (float.IsNaN(z))
            {
                continue;
            }

            zMin = Math.Min(zMin, z);
            zMax = Math.Max(zMax, z);
        }

        if (zMin > zMax)
        {
            zMin = zMax = 0f;
        }

        var span = zMax - zMin;
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Magic);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((float)cellSizeCm);
        writer.Write((float)originXcm);
        writer.Write((float)originYcm);
        writer.Write(width);
        writer.Write(height);
        writer.Write(zMin);
        writer.Write(zMax);
        writer.Write(NoData);
        writer.Write((ushort)0);
        foreach (var z in heightsCm)
        {
            if (float.IsNaN(z))
            {
                writer.Write(NoData);
            }
            else if (span <= 0f)
            {
                writer.Write((ushort)0);
            }
            else
            {
                var raw = (int)Math.Round((z - zMin) / span * MaxRaw, MidpointRounding.AwayFromZero);
                writer.Write((ushort)Math.Clamp(raw, 0, MaxRaw));
            }
        }
    }
}
