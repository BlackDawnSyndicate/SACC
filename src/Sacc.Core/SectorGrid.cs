using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum GridStyle
{
    Off,
    /// <summary>Base-game sectors: lines and labels (e.g. "B2").</summary>
    Sectors,
    /// <summary>Sector lines and labels, plus thin keypad lines.</summary>
    SectorsAndKeypads,
    /// <summary>Keypad lines with sector+keypad labels (e.g. "B27").</summary>
    Keypads,
    /// <summary>The 225 keypad cells named as one 15×15 grid, A0 at the top-left.</summary>
    MapKeypads,
    /// <summary>As MapKeypads, counted from the bottom-right.</summary>
    MapKeypadsInverted,
}

/// <summary>
/// SCUM's sector grid. The island is 5×5 sectors: rows Z, A, B, C, D from south to north,
/// columns 0–4 from east to west, as on the in-game map. Each sector splits 3×3 into "keypad"
/// cells numbered like a numeric keypad (7 8 9 / 4 5 6 / 1 2 3), so B27 is the top-left
/// ninth of sector B2. Works in map units (x right, y down), so it follows whatever world
/// orientation the projection uses.
/// </summary>
public sealed partial class SectorGrid
{
    public const int Sectors = 5;
    public const int KeypadDivisions = 3;
    private static readonly string[] RowLettersTopDown = { "D", "C", "B", "A", "Z" };

    private readonly MapProjection _projection;
    public SectorGrid(MapProjection projection) => _projection = projection;

    /// <summary>The Island's bounds; lets sectors be named before map data has loaded.</summary>
    public static readonly MapSettings IslandSettings = new(2, 6, 1280, 0, 320, 0, 320, 619200, -904800, 619200, -904800);
    public static SectorGrid Island { get; } = new(new MapProjection(IslandSettings));

    public double SectorWidth => _projection.MapWidth / Sectors;
    public double SectorHeight => _projection.MapHeight / Sectors;

    public readonly record struct Cell(int Col, int Row, int KeyCol, int KeyRow)
    {
        /// <summary>Keypad digit 1–9, numeric-keypad layout (7 at top-left).</summary>
        public int Digit => (KeypadDivisions - 1 - KeyRow) * KeypadDivisions + KeyCol + 1;
        public string Sector => RowLettersTopDown[Row] + (Sectors - 1 - Col);
        public string Keypad => Sector + Digit;
        /// <summary>Column and row in the 15×15 keypad grid.</summary>
        public int FineCol => Col * KeypadDivisions + KeyCol;
        public int FineRow => Row * KeypadDivisions + KeyRow;
    }

    public Cell? CellAtMap(double mapX, double mapY)
    {
        if (!_projection.Contains(mapX, mapY)) return null;
        var fx = Math.Min(Sectors * KeypadDivisions - 1, (int)Math.Floor(mapX / (SectorWidth / KeypadDivisions)));
        var fy = Math.Min(Sectors * KeypadDivisions - 1, (int)Math.Floor(mapY / (SectorHeight / KeypadDivisions)));
        return new Cell(fx / KeypadDivisions, fy / KeypadDivisions, fx % KeypadDivisions, fy % KeypadDivisions);
    }

    public Cell? CellAtWorld(double worldX, double worldY)
    {
        var (mx, my) = _projection.ToMap(worldX, worldY);
        return CellAtMap(mx, my);
    }

    /// <summary>"B27" for a world position, or "" outside the island.</summary>
    public string KeypadName(double worldX, double worldY) => CellAtWorld(worldX, worldY)?.Keypad ?? "";

    /// <summary>Name of a cell in the whole-map 15×15 scheme: letter for the row, number for the column.</summary>
    public static string MapKeypadName(int fineCol, int fineRow, bool inverted)
    {
        var n = Sectors * KeypadDivisions;
        var row = inverted ? n - 1 - fineRow : fineRow;
        var col = inverted ? n - 1 - fineCol : fineCol;
        return ((char)('A' + row)).ToString() + col;
    }

    [GeneratedRegex(@"^\s*([DCBAZ])\s*([0-4])\s*([1-9])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SectorOrKeypad();

    /// <summary>
    /// Parse "B2" (a sector) or "B27" (a keypad cell) into the map-unit rectangle it covers.
    /// </summary>
    public bool TryParse(string? text, out (double Left, double Top, double Width, double Height) rect, out bool isKeypad)
    {
        rect = default;
        isKeypad = false;
        var m = SectorOrKeypad().Match(text ?? "");
        if (!m.Success) return false;
        var row = Array.IndexOf(RowLettersTopDown, m.Groups[1].Value.ToUpperInvariant());
        var col = Sectors - 1 - int.Parse(m.Groups[2].Value);
        var left = col * SectorWidth;
        var top = row * SectorHeight;
        if (!m.Groups[3].Success)
        {
            rect = (left, top, SectorWidth, SectorHeight);
            return true;
        }
        var digit = int.Parse(m.Groups[3].Value);
        var keyCol = (digit - 1) % KeypadDivisions;
        var keyRow = KeypadDivisions - 1 - (digit - 1) / KeypadDivisions;
        var w = SectorWidth / KeypadDivisions;
        var h = SectorHeight / KeypadDivisions;
        rect = (left + keyCol * w, top + keyRow * h, w, h);
        isKeypad = true;
        return true;
    }

    /// <summary>World centre of a sector or keypad cell name.</summary>
    public Vec3? CenterOf(string? text)
    {
        if (!TryParse(text, out var r, out _)) return null;
        return _projection.ToWorld(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }
}
