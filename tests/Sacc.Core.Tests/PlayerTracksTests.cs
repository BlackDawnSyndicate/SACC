using Sacc.Core;
using Sacc.Core.Model;
using Xunit;

namespace Sacc.Core.Tests;

public class PlayerTracksTests
{
    private const string Id = "76561198000000001";
    private const string Other = "76561198000000002";
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(-5));

    private static PlayerRecord At(double x, string id = Id, string name = "Phoenix") =>
        new() { SteamId = id, Nickname = name, Location = new Vec3(x, 0, 100) };

    [Fact]
    public void Add_SkipsStandingStillUntilTheHeartbeat()
    {
        var t = new PlayerTracks();
        Assert.Single(t.Add(new[] { At(0) }, T0));
        Assert.Empty(t.Add(new[] { At(50) }, T0.AddMinutes(1)));               // under 1 m
        Assert.Single(t.Add(new[] { At(50) }, T0.AddMinutes(5)));              // heartbeat
        Assert.Single(t.Add(new[] { At(1000) }, T0.AddMinutes(6)));            // moved
        Assert.Empty(t.Add(new[] { new PlayerRecord { SteamId = Other } }, T0)); // no position in the list
        Assert.Equal(3, t.PointCount);
    }

    [Fact]
    public void Segments_BreakWhereThePlayerLeftAndHonourTheWindow()
    {
        var t = new PlayerTracks();
        t.Add(new[] { At(0) }, T0);
        t.Add(new[] { At(1000) }, T0.AddMinutes(1));
        t.Add(new[] { At(90000) }, T0.AddHours(3));
        t.Add(new[] { At(1000, Other, "Someone") }, T0);

        var mine = t.Segments(Id, DateTimeOffset.MinValue, TimeSpan.FromMinutes(6));
        Assert.Equal(new[] { 2, 1 }, mine.Select(r => r.Count));
        Assert.Single(t.Segments(Id, T0.AddHours(1), TimeSpan.FromMinutes(6)));
        Assert.Equal(3, t.Segments(null, DateTimeOffset.MinValue, TimeSpan.FromMinutes(6)).Count);
    }

    [Theory]
    [InlineData("Phoenix")]
    [InlineData("Smith, \"Doc\"")]
    [InlineData("=HYPERLINK(1)")]
    [InlineData("")]
    public void Csv_RoundTripsAnyName(string name)
    {
        var p = new TrackPoint(T0, Id, name, new Vec3(-525564.188, -275570, 13831.64));
        Assert.Equal(p, PlayerTracks.ParseCsv(PlayerTracks.ToCsv(p)));
    }

    [Fact]
    public void Csv_NeutralisesFormulasAndControlCharacters()
    {
        var line = PlayerTracks.ToCsv(new TrackPoint(T0, Id, "=cmd()\nx", new Vec3(1, 2, 3)));
        Assert.Equal($"2026-11-04T12:00:00-05:00,{Id},'=cmd()x,1,2,3", line);
    }

    [Fact]
    public void Load_SkipsTheHeaderAndDamagedLines()
    {
        var good = $"2026-11-04T12:00:00-05:00,{Id},Phoenix,1,2,3";
        var t = new PlayerTracks();
        t.Load(new[] { PlayerTracks.CsvHeader, "garbage", $"2026-11-04T12:00:00-05:00,{Id},Phoenix,1,2", good });
        Assert.Equal(1, t.PointCount);
        Assert.Equal(new[] { PlayerTracks.CsvHeader, good }, t.ExportLines(Id));
        Assert.Equal(new[] { PlayerTracks.CsvHeader }, t.ExportLines(Other));
    }
}
