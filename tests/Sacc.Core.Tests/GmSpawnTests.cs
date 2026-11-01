using Sacc.Core;
using Sacc.Core.Model;
using Xunit;

namespace Sacc.Core.Tests;

public class GmSpawnTests
{
    private static readonly Vec3 Spot = new(-53464.275, -767293.117, 116.232);
    private static GmSpawn.Action Act(string key) => GmSpawn.Actions.Single(a => a.Key == key);

    private static CommandDef Def(string template, params ParamDef[] ps) => new() { Id = "t", Template = template, Params = ps.ToList() };
    private static ParamDef P(string name, ParamKind kind = ParamKind.Text, bool optional = false) => new() { Name = name, Kind = kind, Optional = optional };

    [Fact]
    public void Zombies_UseTypeCountAndLocation()
    {
        var def = Def("#SpawnZombie {zombie} {amount}[ Location \"{coords}\"]", P("zombie"), P("amount", ParamKind.Integer), P("coords", ParamKind.Message, true));
        var r = GmSpawn.Render(def, Act("zombies"), "BP_Zombie_Nuclear", 5, Spot);
        Assert.True(r.Ok);
        Assert.Equal("#SpawnZombie BP_Zombie_Nuclear 5 Location \"-53464.275 -767293.117 116.232\"", r.Text);
    }

    [Fact]
    public void Beepers_AreSuicideVestZombies()
    {
        var def = Def("#SpawnZombie {zombie} {amount}[ Location \"{coords}\"]", P("zombie"), P("amount", ParamKind.Integer), P("coords", ParamKind.Message, true));
        Assert.StartsWith("#SpawnZombie BP_Zombie_SuicideVest 2 ", GmSpawn.Render(def, Act("beepers"), "ignored", 2, Spot).Text);
    }

    [Fact]
    public void ActionsWithoutACount_Repeat()
    {
        var def = Def("#SpawnRazor \"{coords}\"", P("coords", ParamKind.Message));
        var r = GmSpawn.Render(def, Act("razors"), null, 3, Spot);
        Assert.Equal(3, r.Text.Split('\n').Length);
    }

    [Fact]
    public void EntryWithoutCoords_IsRefused()
    {
        var def = Def("#SpawnRazor");
        Assert.False(GmSpawn.Render(def, Act("razors"), null, 1, Spot).Ok);
    }

    [Fact]
    public void KitAt_AppendsLocationAfterStackCount()
    {
        var lines = GmSpawn.KitAt(new[] { "#SpawnItem Magazine_M16 3 StackCount 200", "#spawnvehicle BPC_RIS" }, Spot, out var notPlaced);
        Assert.Equal("#SpawnItem Magazine_M16 3 StackCount 200 Location \"-53464.275 -767293.117 116.232\"", lines[0]);
        Assert.Equal("#spawnvehicle BPC_RIS", lines[1]);
        Assert.Equal(1, notPlaced);
    }

    [Fact]
    public void NearbyHeight_PicksTheClosestMarkerWithAHeight()
    {
        var markers = new[]
        {
            new MapMarker(1, 1, "far", 10, 10, 900),
            new MapMarker(2, 1, "near", 101, 100, 4500),
            new MapMarker(3, 1, "no height", 100, 100, null),
        };
        Assert.Equal(4500, GmSpawn.NearbyHeight(markers, 100, 100, 6));
        Assert.Null(GmSpawn.NearbyHeight(markers, 200, 200, 6));
    }

    [Fact]
    public void ClimbHeights_GoFromBelowToAboveInSteps()
    {
        Assert.Equal(new[] { 9000d, 9300, 9600, 9900, 10200, 10500, 10800 }, GmSpawn.ClimbHeights(10000, 1000, 1000, 300));
        Assert.Equal(21, GmSpawn.ClimbHeights(0, 3000, 3000, 300).Count);
        Assert.Equal(new[] { -100d, -50, 0 }, GmSpawn.ClimbHeights(0, 100, 0, 1)); // steps never go below 50 cm
    }

    [Fact]
    public void ClimbPlan_TriesTheGuessThenAboveThenBelow()
    {
        var plan = GmSpawn.ClimbPlan(10000, 1000, 500);
        Assert.Equal(new[] { 9000d, 9500, 10000, 10500, 11000 }, plan.Take(5));                       // guess ± range, from below
        Assert.Equal(new[] { 11500d, 12000, 12500, 13000, 13500, 14000 }, plan.Skip(5).Take(6));    // then up to guess + 4×range
        Assert.Equal(new[] { 6000d, 6500, 7000, 7500, 8000, 8500 }, plan.Skip(11));                  // then guess − 4×range … − range
        Assert.Equal(plan.Count, plan.Distinct().Count());
    }

    [Fact]
    public void EstimateHeight_WeighsTheNearestMarkersAtAnyDistance()
    {
        var markers = new[]
        {
            new MapMarker(1, 1, "a", 0, 0, 1000),
            new MapMarker(2, 1, "b", 10, 0, 3000),
            new MapMarker(3, 1, "no height", 5, 0, null),
            new MapMarker(4, 1, "far", 300, 300, 99999),
        };
        Assert.Equal(2000, GmSpawn.EstimateHeight(markers, 5, 0, out var far, k: 2)!.Value, 6); // equidistant → average
        Assert.Equal(5, far);
        Assert.True(GmSpawn.EstimateHeight(markers, 1, 0, out _, k: 2) < 1100);                    // closer marker dominates
        Assert.Null(GmSpawn.EstimateHeight(Array.Empty<MapMarker>(), 0, 0, out _));
    }

    [Fact]
    public void ParseMarkers_TreatsZeroHeightAsUnknown()
    {
        const string json = """
            {"data":{"mapTemplate":{"layer":{"markerList":[
            {"id":1,"categoryId":2,"title":"Village","longitude":1,"latitude":2,"ingameHeight":0},
            {"id":2,"categoryId":2,"title":"Cave","longitude":1,"latitude":2,"ingameHeight":-120.5},
            {"id":3,"categoryId":2,"title":"Bunker","longitude":1,"latitude":2,"ingameHeight":36967}]}}}}
            """;
        Assert.Equal(new double?[] { null, -120.5, 36967 }, ScumMapClient.ParseMarkers(json).Select(m => m.Height));
    }

    [Fact]
    public void CountRefusals_CountsEachRefusedNpc()
    {
        const string log = "[t]LogSCUM: Sender player state null for message Cannot spawn at the specified location.!\r\n"
                         + "[t]LogSCUM: Sender player state null for message Cannot spawn at the specified location.!\r\n"
                         + "[t]LogSCUM: Sender player state null for message Spawned Soap.!\r\n";
        Assert.Equal(2, GmSpawn.CountRefusals(log));
        Assert.Equal(0, GmSpawn.CountRefusals(""));
        Assert.True(GmSpawn.Actions.Single(a => a.Key == "npcs").GroundChecked);
        Assert.False(GmSpawn.Actions.Single(a => a.Key == "zombies").GroundChecked);
    }

    [Theory]
    [InlineData("#spawnanimal BP_Wolf3", "#spawnanimal BP_Wolf")]
    [InlineData("#SpawnAnimal BP_Bear2 3", "#SpawnAnimal BP_Bear 3")]
    [InlineData("#SpawnAnimal BP_Wolf_Mutant 2", "#SpawnAnimal BP_Wolf_Mutant 2")]
    [InlineData("#SpawnItem BP_Wolf3 1", "#SpawnItem BP_Wolf3 1")]
    public void FixAnimalCommand_RenamesOldBlueprints(string line, string expected) =>
        Assert.Equal(expected, GmSpawn.FixAnimalCommand(line));

    [Fact]
    public void Migrate_FixesOldAnimalNames()
    {
        var p = new Profile { Version = 1 };
        p.Settings.Gm["animals"] = new GmChoice { Type = "BP_Wolf3", Count = 3 };
        p.Kits.Add(new Kit { Items = { new KitItem { Code = "#spawnanimal BP_Deer2" }, new KitItem { Code = "Magazine_M16" } } });
        p.Macros.Add(new Macro { Script = "// zoo\r\n#SpawnAnimal BP_Horse2 2\r\nwait 500" });
        p.Migrate();
        Assert.Equal("BP_Wolf", p.Settings.Gm["animals"].Type);
        Assert.Equal("#spawnanimal BP_Deer", p.Kits[0].Items[0].Code);
        Assert.Equal("Magazine_M16", p.Kits[0].Items[1].Code);
        Assert.Equal("// zoo\r\n#SpawnAnimal BP_Horse 2\r\nwait 500", p.Macros[0].Script);
    }
}
