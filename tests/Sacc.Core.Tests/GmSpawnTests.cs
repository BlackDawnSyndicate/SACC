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
