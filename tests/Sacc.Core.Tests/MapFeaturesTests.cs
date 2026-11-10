using System.IO;
using System.Text;
using Sacc.Core;
using Xunit;

namespace Sacc.Core.Tests;

public class MapFeaturesTests
{
    private static MapFeatures Parse(string json) =>
        MapFeatures.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ReadsHuntingAreasAndThreatZones()
    {
        var f = Parse("""
            {
              "version": 1,
              "huntingAreas": [ { "x": 1000, "y": -2000, "r": 5000, "biome": "BD_Village" } ],
              "threatZones": [ { "x": 300, "y": 400, "rx": 1200, "ry": 800, "yaw": 45, "settings": "HTZ_Military_HotSpot" } ]
            }
            """);

        Assert.Single(f.HuntingAreas);
        Assert.Single(f.ThreatZones);
        var h = f.HuntingAreas[0];
        Assert.Equal(1000, h.X);
        Assert.Equal(-2000, h.Y);
        Assert.Equal(5000, h.R);
        Assert.Equal("BD_Village", h.Biome);
        var z = f.ThreatZones[0];
        Assert.Equal(1200, z.Rx);
        Assert.Equal(800, z.Ry);
        Assert.Equal(45, z.Yaw);
    }

    [Theory]
    [InlineData("HTZ_Military_HotSpot", ThreatTier.High)]
    [InlineData("MTZ_Settlement_Large", ThreatTier.Medium)]
    [InlineData("LTZ_Something", ThreatTier.Low)]
    [InlineData("None", ThreatTier.None)]
    [InlineData("", ThreatTier.None)]
    public void ThreatTierComesFromSettingsPrefix(string settings, ThreatTier expected) =>
        Assert.Equal(expected, new ThreatZone(0, 0, 1, 1, 0, settings).Tier);

    [Theory]
    [InlineData("BD_MutantDen_Bear", true)]
    [InlineData("BD_Village", false)]
    public void MutantDenIsDetected(string biome, bool den) =>
        Assert.Equal(den, new HuntingArea(0, 0, 1, biome).IsMutantDen);

    [Fact]
    public void ReadsLocationsAndPatrolPaths()
    {
        var f = Parse("""
            {
              "version": 1,
              "locations": [ { "x": 100, "y": 200, "name": "Outpost" } ],
              "patrolPaths": [ { "points": [[0,0],[500,0],[500,500]] } ]
            }
            """);
        Assert.Single(f.Locations);
        Assert.Equal("Outpost", f.Locations[0].Name);
        Assert.Equal(100, f.Locations[0].X);
        Assert.Single(f.PatrolPaths);
        Assert.Equal(3, f.PatrolPaths[0].Points.Count);
        Assert.Equal((500.0, 500.0), f.PatrolPaths[0].Points[2]);
    }

    [Fact]
    public void DropsPatrolPathsWithFewerThanTwoPoints()
    {
        var f = Parse("""{ "version": 1, "patrolPaths": [ { "points": [[0,0]] }, { "points": [] } ] }""");
        Assert.Empty(f.PatrolPaths);
    }

    [Fact]
    public void MissingArraysBecomeEmpty()
    {
        var f = Parse("""{ "version": 1 }""");
        Assert.Empty(f.HuntingAreas);
        Assert.Empty(f.ThreatZones);
        Assert.Empty(f.Locations);
        Assert.Empty(f.PatrolPaths);
    }

    [Fact]
    public void BadJsonThrowsInvalidData() =>
        Assert.Throws<InvalidDataException>(() => Parse("not json at all"));
}
