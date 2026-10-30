using Sacc.Core;

namespace Sacc.Core.Tests;

public class SectorGridTests
{
    private static readonly SectorGrid Grid = SectorGrid.Island;

    [Theory]
    // Map units (x right, y down). Layout confirmed on scum-map.com and in game:
    // D4 at the top-left, Z0 at the bottom-right; numbers fall eastward, letters rise northward.
    [InlineData(10, 10, "D4")]
    [InlineData(310, 10, "D0")]
    [InlineData(10, 310, "Z4")]
    [InlineData(310, 310, "Z0")]
    [InlineData(160, 160, "B2")]
    [InlineData(70, 200, "A3")]
    public void SectorNames(double mx, double my, string expected) =>
        Assert.Equal(expected, Grid.CellAtMap(mx, my)!.Value.Sector);

    [Theory]
    // Inside sector B2 (map x 128..192, y 128..192), keypad cells are 21.33 units.
    [InlineData(130, 130, "B27")] // top-left
    [InlineData(160, 130, "B28")]
    [InlineData(190, 130, "B29")]
    [InlineData(160, 160, "B25")] // centre
    [InlineData(130, 190, "B21")] // bottom-left
    [InlineData(190, 190, "B23")]
    // The column left of B2 is B3's right-hand column: 9, 6, 3, as scum-map labels it.
    [InlineData(127, 130, "B39")]
    [InlineData(127, 190, "B33")]
    public void KeypadNames(double mx, double my, string expected) =>
        Assert.Equal(expected, Grid.CellAtMap(mx, my)!.Value.Keypad);

    [Fact]
    public void WorldPositions()
    {
        // Centre of the world box is the centre of B2.
        Assert.Equal("B25", Grid.KeypadName(-142800, -142800));
        // +X is west, so the world's top-left corner (max X, max Y) is D4.
        Assert.Equal("D47", Grid.KeypadName(619000, 619000));
        Assert.Equal("Z03", Grid.KeypadName(-904000, -904000));
        Assert.Equal("", Grid.KeypadName(700000, 0));
    }

    [Fact]
    public void ParseAndRoundTripEveryCell()
    {
        foreach (var row in "DCBAZ")
        for (var n = 0; n <= 4; n++)
        {
            var sector = $"{row}{n}";
            var c = Grid.CenterOf(sector)!.Value;
            Assert.Equal(sector + "5", Grid.KeypadName(c.X, c.Y));
            for (var d = 1; d <= 9; d++)
            {
                var name = $"{sector}{d}";
                var k = Grid.CenterOf(name)!.Value;
                Assert.Equal(name, Grid.KeypadName(k.X, k.Y));
            }
        }
    }

    [Theory]
    [InlineData("b2", true, false)]
    [InlineData(" B 2 7 ", true, true)]
    [InlineData("E2", false, false)]
    [InlineData("B5", false, false)]
    [InlineData("B20", false, false)]
    [InlineData("", false, false)]
    public void Parse(string text, bool ok, bool keypad)
    {
        Assert.Equal(ok, Grid.TryParse(text, out _, out var isKeypad));
        Assert.Equal(keypad, isKeypad);
    }

    [Fact]
    public void MapKeypadNames()
    {
        Assert.Equal("A0", SectorGrid.MapKeypadName(0, 0, inverted: false));
        Assert.Equal("C14", SectorGrid.MapKeypadName(14, 2, inverted: false));
        Assert.Equal("A0", SectorGrid.MapKeypadName(14, 14, inverted: true));
        Assert.Equal("O14", SectorGrid.MapKeypadName(0, 0, inverted: true));
    }
}
