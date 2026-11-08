using Sacc.Core;
using Sacc.Core.Model;
using Xunit;

namespace Sacc.Core.Tests;

public class MissionEngineTests
{
    private static readonly Vec3 Pin = new(1000, 1000, 0);
    private static Vec3 MetresAway(double m) => new(1000 + m * 100, 1000, 500);

    private static CommandDef Catalog(string id)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SACC.sln"))) dir = dir.Parent;
        return JsonStore.LoadCatalog(Path.Combine(dir!.FullName, "data", "commands.json")).Find(id)!;
    }

    [Fact]
    public void Decide_NoRulesFiresWithoutKnowingWhereAnyoneIs()
    {
        Assert.Equal(MissionEngine.Decision.Fire, MissionEngine.Decide(new MissionSpawn { At = Pin }, null));
    }

    [Fact]
    public void Decide_UnknownPositionsNeverFireAPinWithARule()
    {
        Assert.Equal(MissionEngine.Decision.WaitForPlayer, MissionEngine.Decide(new MissionSpawn { At = Pin, HoldRadiusM = 50 }, null));
        Assert.Equal(MissionEngine.Decision.WaitForPlayer, MissionEngine.Decide(new MissionSpawn { At = Pin, TriggerRadiusM = 50 }, null));
    }

    [Fact]
    public void Decide_TriggerAndHoldMakeARing()
    {
        var s = new MissionSpawn { At = Pin, TriggerRadiusM = 100, HoldRadiusM = 30 };
        Assert.Equal(MissionEngine.Decision.WaitForPlayer, MissionEngine.Decide(s, new[] { MetresAway(150) }));
        Assert.Equal(MissionEngine.Decision.Fire, MissionEngine.Decide(s, new[] { MetresAway(60) }));
        Assert.Equal(MissionEngine.Decision.Held, MissionEngine.Decide(s, new[] { MetresAway(20) }));
        // One player in the ring and another on top: still held.
        Assert.Equal(MissionEngine.Decision.Held, MissionEngine.Decide(s, new[] { MetresAway(60), MetresAway(5) }));
        Assert.Equal(MissionEngine.Decision.WaitForPlayer, MissionEngine.Decide(s, Array.Empty<Vec3>()));
    }

    [Fact]
    public void RecordSend_SilenceIsNotASpawn()
    {
        var s = new MissionSpawn { Type = "BP_Wolf", Count = 3 };
        MissionEngine.RecordSend(s, 1, default);
        Assert.Equal(MissionSpawnState.Sent, s.State);
        Assert.Equal(0, s.Spawned);
    }

    [Fact]
    public void RecordSend_ReadsTheGamesReplies()
    {
        const string log =
            "LogSCUM: Sender player state null for message Spawned Magazine_M1.!\n" +
            "LogSCUM: Sender player state null for message 'BP_Wolf3' is not allowed to be spawned.!\n" +
            "LogSCUM: Sender player state null for message Cannot spawn at the specified location.!\n";
        Assert.Equal(new MissionEngine.Replies(1, 1, 1), MissionEngine.ReadReplies(log));

        var ok = new MissionSpawn { Type = "Magazine_M1", Count = 4 };
        MissionEngine.RecordSend(ok, 1, new MissionEngine.Replies(1, 0, 0));
        Assert.Equal((MissionSpawnState.Spawned, 4), (ok.State, ok.Spawned));

        var no = new MissionSpawn { Type = "BP_Wolf3", Count = 1 };
        MissionEngine.RecordSend(no, 1, new MissionEngine.Replies(0, 0, 1));
        Assert.Equal((MissionSpawnState.Refused, 0, 1), (no.State, no.Spawned, no.Refused));
    }

    [Fact]
    public void RecordClimb_CountsWhatWasntRefused()
    {
        var s = new MissionSpawn { Count = 10 };
        MissionEngine.RecordClimb(s, 7);
        Assert.Equal((MissionSpawnState.Partial, 7, 3), (s.State, s.Spawned, s.Refused));
        MissionEngine.RecordClimb(s, 0);
        Assert.Equal(MissionSpawnState.Refused, s.State);
        MissionEngine.RecordClimb(s, 10);
        Assert.Equal(MissionSpawnState.Spawned, s.State);
    }

    [Fact]
    public void Tally_CountsOnlyWhatActuallySpawned()
    {
        var a = new MissionSpawn { Type = "BP_Zombie", Count = 10 };
        MissionEngine.RecordClimb(a, 6);
        var b = new MissionSpawn { Type = "bp_zombie", Count = 5 };
        MissionEngine.RecordClimb(b, 5);
        var refused = new MissionSpawn { Type = "BP_Drifter", Count = 2 };
        MissionEngine.RecordClimb(refused, 0);
        var silent = new MissionSpawn { Type = "BP_Wolf", Count = 3 };
        MissionEngine.RecordSend(silent, 1, default);
        var pending = new MissionSpawn { Type = "BP_Bear", Count = 9 };

        var rows = MissionEngine.Tally(new[] { a, b, refused, silent, pending });
        Assert.Equal(new[] { "BP_Drifter", "BP_Wolf", "BP_Zombie" }, rows.Select(r => r.Type));
        Assert.Equal(new MissionEngine.TallyRow("BP_Zombie", 15, 11, 4, 0), rows[2]);
        Assert.Equal(new MissionEngine.TallyRow("BP_Drifter", 2, 0, 2, 0), rows[0]);
        Assert.Equal(new MissionEngine.TallyRow("BP_Wolf", 3, 0, 0, 3), rows[1]);
    }

    [Fact]
    public void Cleanup_TeleportsThenDestroysEachSpawnedItemType()
    {
        var phase = new MissionPhase { Center = new Vec3(100, 200, 300), RadiusM = 149.5, ExtraCleanup = "#MyCommand\r\n\r\n" };
        var item = new MissionSpawn { Type = "Magazine_M1", State = MissionSpawnState.Spawned, Spawned = 1, ItemCodes = { "Magazine_M1" } };
        var same = new MissionSpawn { Type = "magazine_m1", State = MissionSpawnState.Spawned, Spawned = 1, ItemCodes = { "magazine_m1" } };
        var unfired = new MissionSpawn { Type = "Soap", ItemCodes = { "Soap" } };
        var npc = new MissionSpawn { Type = "BP_Drifter", State = MissionSpawnState.Spawned, Spawned = 2 };
        var refused = new MissionSpawn { Type = "BP_Guard", State = MissionSpawnState.Refused, Refused = 2 };
        foreach (var s in new[] { item, same, unfired, npc, refused }) phase.Spawns.Add(s);

        var plan = MissionEngine.Cleanup(phase, Catalog("teleport"), Catalog("destroy-items-radius"));
        Assert.Null(plan.Error);
        Assert.Equal(new[] { "#Teleport 100 200 300", "#DestroyAllItemsWithinRadius Magazine_M1 150", "#MyCommand" }, plan.Lines);
        Assert.Equal(new[] { "BP_Drifter" }, plan.NotCovered);
    }

    [Fact]
    public void ItemsDestroyed_SumsTheGamesReplies()
    {
        Assert.Equal(5, MissionEngine.ItemsDestroyed("x for message 0 items destroyed!!\ny for message 5 items destroyed!!\n"));
    }

    [Fact]
    public void Expand_PutsMembersInARingAroundTheAnchor()
    {
        var prefab = new MissionPrefab { Name = "Rager patrol" };
        prefab.Members.Add(new PrefabMember { Kind = "vehicle", Type = "BPC_Rager" });
        prefab.Members.Add(new PrefabMember { Kind = "npcs", Type = "BP_Drifter_Lvl_5", Copies = 6, OffsetM = 5 });

        var anchor = new Vec3(1000, 2000, 300);
        var pins = MissionEngine.Expand(prefab, anchor, triggerRadiusM: 100, holdRadiusM: 30);

        Assert.Equal(7, pins.Count);
        Assert.Equal(anchor, pins[0].At);
        var ring = pins.Skip(1).ToList();
        Assert.All(ring, p => Assert.Equal(500, MissionEngine.Distance(p.At, anchor), 1));
        Assert.Equal(6, ring.Select(p => (p.At.X, p.At.Y)).Distinct().Count());
        Assert.All(pins, p => Assert.Equal(("Rager patrol", 100d, 30d), (p.Group, p.TriggerRadiusM, p.HoldRadiusM)));
    }

    [Fact]
    public void Export_OneBlockPerPhaseWithRulesAsCommentsAndPlannedCleanup()
    {
        var mission = new Mission { Name = "Night raid" };
        var phase = new MissionPhase { Name = "Approach", Center = new Vec3(10, 20, 30), RadiusM = 100, Announce = "Hostiles inbound" };
        phase.Spawns.Add(new MissionSpawn { Kind = "zombies", Type = "BP_Zombie", TriggerRadiusM = 80 });
        phase.Spawns.Add(new MissionSpawn { Kind = "item", Type = "Soap" });
        mission.Phases.Add(phase);
        mission.Phases.Add(new MissionPhase { Name = "Extract" });

        var e = MissionEngine.ToWhalleyBot(mission,
            s => new[] { s.Kind == "item" ? $"#SpawnItem {s.Type} 1 Location \"1 2 3\"" : $"#SpawnZombie {s.Type} 1 Location \"1 2 3\"" },
            Catalog("announce"), Catalog("teleport"), Catalog("destroy-items-radius"), 10);

        Assert.Contains("phase 1 of 2: Approach", e.Text);
        Assert.Contains("phase 2 of 2: Extract", e.Text);
        Assert.Contains("#Announce Hostiles inbound", e.Text);
        Assert.Contains("// Only when a player is within 80 m in SACC", e.Text);
        Assert.Contains("#SpawnZombie BP_Zombie 1 Location \"1 2 3\"", e.Text);
        Assert.Contains("#DestroyAllItemsWithinRadius Soap 100", e.Text);
        Assert.DoesNotContain("#DestroyAllItemsWithinRadius BP_Zombie", e.Text);
        Assert.Contains(e.Notes, n => n.StartsWith("Radius rules became comments"));
    }

    [Fact]
    public void Missions_SurviveTheProfileFile()
    {
        var mission = new Mission { Name = "M" };
        var phase = new MissionPhase { Name = "P", Center = new Vec3(1, 2, 3) };
        var pin = new MissionSpawn { Kind = "npcs", Type = "BP_Drifter_Lvl_5", Count = 2, At = new Vec3(4, 5, 6), HoldRadiusM = 30 };
        MissionEngine.RecordClimb(pin, 1);
        pin.ItemCodes.Add("Soap");
        phase.Spawns.Add(pin);
        mission.Phases.Add(phase);

        var path = Path.GetTempFileName();
        try
        {
            JsonStore.Save(path, new Profile { Missions = { mission } });
            var loaded = JsonStore.LoadOrDefault<Profile>(path).Missions.Single().Phases.Single();
            Assert.Equal(new Vec3(1, 2, 3), loaded.Center);
            var p = loaded.Spawns.Single();
            Assert.Equal((new Vec3(4, 5, 6), 30d, MissionSpawnState.Partial, 1, 1), (p.At, p.HoldRadiusM, p.State, p.Spawned, p.Refused));
            Assert.Equal(new[] { "Soap" }, p.ItemCodes);
        }
        finally { File.Delete(path); }
    }
}
