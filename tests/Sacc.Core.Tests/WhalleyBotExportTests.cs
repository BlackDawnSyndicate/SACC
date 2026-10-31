using Sacc.Core;
using Sacc.Core.Model;
using Xunit;

namespace Sacc.Core.Tests;

public class WhalleyBotExportTests
{
    [Fact]
    public void MacroToTask_ConvertsWaitsVariablesAndSplits()
    {
        var e = WhalleyBotExport.MacroToTask(
            "// storm\n#Announce Storm\nwait 1500\n#SpawnItem Apple 12 Location {player}\nwait 0\n#Teleport 1 2 3 {me}", "76561198000000009", 10);
        Assert.Equal(
            "// storm\n#Announce Storm\n#taskpause 2\n#SpawnItem Apple 10 Location <argument>\n#SpawnItem Apple 2 Location <argument>\n#Teleport 1 2 3 76561198000000009",
            e.Text);
        Assert.Contains(e.Notes, n => n.Contains("#ftask"));
        Assert.Contains(e.Notes, n => n.Contains("#taskpause"));
    }

    [Fact]
    public void MacroToTask_FlagsUnknownVariablesAndMissingSteamId()
    {
        var e = WhalleyBotExport.MacroToTask("#SetTime {hour}\n#Location {me}", null, 10);
        Assert.Equal("#SetTime <hour>\n#Location {me}", e.Text);
        Assert.Contains(e.Notes, n => n.Contains("#var_hour"));
        Assert.Contains(e.Notes, n => n.Contains("SteamID64"));
    }

    [Fact]
    public void MacroToTask_LeavesMultiItemSpawnsWhole()
    {
        var e = WhalleyBotExport.MacroToTask("#spawnitem {C4_Pack 1;TNT 30} Location \"<coords>\"", null, 10);
        Assert.Equal("#spawnitem {C4_Pack 1;TNT 30} Location \"<coords>\"", e.Text);
    }

    [Fact]
    public void SpotLinesAndCoordinateLists_UseInvariantCoordinates()
    {
        var p = new Vec3(-53464.275, -767293.117, 116.232);
        Assert.Equal("#spawnrazor \"-53464.275 -767293.117 116.232\"", WhalleyBotExport.SpotLine("razor", p));
        Assert.Equal("#location \"-53464.275 -767293.117 116.232\"", WhalleyBotExport.SpotLine("location", p));
        Assert.Null(WhalleyBotExport.SpotLine("nope", p));
        Assert.Equal("#var_coords {1 2 3;-4.5 6 0}",
            WhalleyBotExport.CoordList("coords", new[] { new Vec3(1, 2, 3), new Vec3(-4.5, 6, 0) }));
    }
}
