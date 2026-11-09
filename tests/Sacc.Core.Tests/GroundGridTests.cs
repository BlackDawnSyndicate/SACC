using System.IO;
using Sacc.Core;
using Xunit;

namespace Sacc.Core.Tests;

public class GroundGridTests
{
    private static GroundGrid RoundTrip(int w, int h, double cell, double ox, double oy, float[] heights)
    {
        using var ms = new MemoryStream();
        GroundGrid.Write(ms, w, h, cell, ox, oy, heights);
        ms.Position = 0;
        return GroundGrid.Read(ms);
    }

    [Fact]
    public void RoundTripsHeaderAndHeights()
    {
        // 3x2 grid, 400 cm cells, origin at (1000, -2000). Row-major y*w+x.
        var heights = new[] { 100f, 250f, 400f, 1200f, float.NaN, -350f };
        var grid = RoundTrip(3, 2, 400, 1000, -2000, heights);

        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(400f, grid.CellSizeCm);
        Assert.Equal(1000f, grid.OriginXCm);
        Assert.Equal(-2000f, grid.OriginYCm);
        Assert.Equal(5, grid.FilledCells); // one NaN

        // Cell centres decode within quantisation error (span 1550 cm / 65534 ≈ 0.024 cm).
        Assert.Equal(100.0, grid.At(1000, -2000)!.Value, 0.1);
        Assert.Equal(250.0, grid.At(1400, -2000)!.Value, 0.1);
        Assert.Equal(400.0, grid.At(1800, -2000)!.Value, 0.1);
        Assert.Equal(1200.0, grid.At(1000, -1600)!.Value, 0.1);
        Assert.Equal(-350.0, grid.At(1800, -1600)!.Value, 0.1);
    }

    [Fact]
    public void NoDataCellReturnsNull()
    {
        var heights = new[] { 100f, 250f, 400f, 1200f, float.NaN, -350f };
        var grid = RoundTrip(3, 2, 400, 1000, -2000, heights);
        Assert.Null(grid.At(1400, -1600)); // the NaN cell (x=1, y=1)
    }

    [Fact]
    public void SnapsToNearestCellAndRejectsOffGrid()
    {
        var grid = RoundTrip(3, 2, 400, 1000, -2000, new[] { 100f, 250f, 400f, 1200f, 800f, -350f });
        // 150 cm past the first cell still rounds to it; 250 past rounds to the next.
        Assert.Equal(100.0, grid.At(1150, -2000)!.Value, 0.1);
        Assert.Equal(250.0, grid.At(1250, -2000)!.Value, 0.1);
        // Half a cell before the origin is off the grid.
        Assert.Null(grid.At(1000 - 201, -2000));
        Assert.Null(grid.At(1000, -2000 - 201));
        Assert.Null(grid.At(1800 + 201, -2000));
    }

    [Fact]
    public void FlatGridDecodesToTheSingleHeight()
    {
        var grid = RoundTrip(2, 2, 400, 0, 0, new[] { 500f, 500f, 500f, 500f });
        Assert.Equal(500.0, grid.At(0, 0)!.Value, 0.001);
        Assert.Equal(500.0, grid.At(400, 400)!.Value, 0.001);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        using var ms = new MemoryStream();
        ms.Write("NOPE"u8.ToArray());
        ms.Write(new byte[64]);
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => GroundGrid.Read(ms));
    }
}
