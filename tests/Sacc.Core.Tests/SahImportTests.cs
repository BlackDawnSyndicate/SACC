using System.IO.Compression;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.Core.Tests;

public class SahImportTests
{
    private static readonly Dictionary<int, string> Map = new() { [10] = "Weapon_M9", [20] = "Batteries" };

    private const string Pack =
        """{"Packages":[{"ID":16,"Title":"Hero - Soldier","Description":"d","Image":"Human","Color":"#FFD17000","Items":{"10":1,"20":5,"99":2},"Commands":null}]}""";

    private static SahImport.Result Import(string text, string name)
    {
        var r = new SahImport.Result();
        SahImport.ImportText(text, name, "Hero_Kits", Map, r);
        return r;
    }

    [Fact]
    public void PackBecomesKit()
    {
        var r = Import(Pack, "Hero - Soldier - SAH Pack (25-04-2026).json");
        var kit = Assert.Single(r.Kits);
        Assert.Equal("Hero - Soldier", kit.Name);
        Assert.Equal("Hero_Kits", kit.Group);
        Assert.Equal(new DateTime(2026, 4, 25), kit.SourceDate);
        Assert.Equal(new[] { "Weapon_M9", "Batteries", null }, kit.Items.Select(i => i.Code));
        Assert.Equal(new[] { 1, 5, 2 }, kit.Items.Select(i => i.Amount));
        Assert.Equal(99, kit.Items[2].SahId);
        Assert.Equal(1, kit.UnresolvedCount);
        Assert.Equal(1, r.FilesRead);
    }

    [Fact]
    public void ResolveFillsLaterMappings()
    {
        var kit = Import(Pack, "p.json").Kits[0];
        var resolved = SahImport.Resolve(new[] { kit }, new Dictionary<int, string>(Map) { [99] = "Canteen" });
        Assert.Equal(1, resolved);
        Assert.Equal("Canteen", kit.Items[2].Code);
    }

    [Fact]
    public void PlayersExport()
    {
        const string json = """
            {"Players":[
              {"ID":1,"Nickname":"Alice","SteamID":"76561198000000001","Role":0,"ServerID":"","Location":"-1.5|20|3","Fakename":"","Fame":"612","Gold":"0","Account":"28673","Status":0},
              {"ID":2,"Nickname":"NoId","SteamID":"","Location":"","Fame":"","Gold":"","Account":""}
            ]}
            """;
        var r = Import(json, "SAH Players (22-03-2026).json");
        var p = Assert.Single(r.Players);
        Assert.Equal("Alice", p.Nickname);
        Assert.Equal(612, p.Fame);
        Assert.Equal(28673, p.Account);
        Assert.Equal(new Vec3(-1.5, 20, 3), p.Location);
        Assert.Equal(new DateTime(2026, 3, 22), p.LastSeen);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void MergeKeepsFresherData()
    {
        var list = new List<PlayerRecord>
        {
            new() { SteamId = "76561198000000001", Nickname = "Live", Fame = 900, LastSeen = new DateTime(2026, 10, 1) },
        };
        SahImport.MergePlayers(list, new[]
        {
            new PlayerRecord { SteamId = "76561198000000001", Nickname = "Old", Fame = 100, Gold = 7, LastSeen = new DateTime(2026, 3, 22) },
            new PlayerRecord { SteamId = "76561198000000002", Nickname = "New" },
        });
        Assert.Equal(2, list.Count);
        Assert.Equal("Live", list[0].Nickname);
        Assert.Equal(900, list[0].Fame);
        Assert.Equal(7, list[0].Gold); // filled from the older export because live data lacked it
    }

    [Fact]
    public void TextLoadouts()
    {
        const string text = """
            Uniform - Urban
            #spawnitem Armor_Tactical_Vest_01_03 1
            #spawnItem Lighter
            #spawnitem Batteries 2
            #SetTime 12


            Uniform - Woodland
            #spawnitem CombatBoots 1
            """;
        var kits = Import(text, "oldloadouts.txt").Kits;
        Assert.Equal(new[] { "Uniform - Urban", "Uniform - Woodland" }, kits.Select(k => k.Name));
        Assert.Equal(new[] { "Armor_Tactical_Vest_01_03", "Lighter", "Batteries" }, kits[0].Items.Select(i => i.Code));
        Assert.Equal(new[] { 1, 1, 2 }, kits[0].Items.Select(i => i.Amount));
        Assert.Equal(new[] { "#SetTime 12" }, kits[0].ExtraCommands);
        Assert.Null(kits[0].Items[0].SahId);
    }

    [Fact]
    public void UnknownJsonAndOtherFilesAreSkipped()
    {
        var r = new SahImport.Result();
        SahImport.ImportText("""{"foo":1}""", "x.json", "", Map, r);
        SahImport.ImportText("{broken", "y.json", "", Map, r);
        SahImport.ImportText("print('hi')", "z.py", "", Map, r);
        Assert.Equal(3, r.FilesSkipped);
        Assert.Empty(r.Kits);
        Assert.Equal(2, r.Warnings.Count);
    }

    [Fact]
    public void FolderWithSubfoldersAndZip()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sacc-import-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Event_Loadouts", "Nest", "Arctic"));
            File.WriteAllText(Path.Combine(root, "Event_Loadouts", "Nest", "Arctic", "Breacher - SAH Pack (30-08-2026).json"), Pack);
            Directory.CreateDirectory(Path.Combine(root, "archive"));
            using (var zip = ZipFile.Open(Path.Combine(root, "archive", "Old.zip"), ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(zip.CreateEntry("Nest - Black/k.json").Open());
                w.Write(Pack);
            }
            File.WriteAllText(Path.Combine(root, "notes.md"), "# hi");

            var r = SahImport.ImportFolder(root, Map);
            Assert.Equal(2, r.Kits.Count);
            Assert.Contains(r.Kits, k => k.Group == "Event_Loadouts/Nest/Arctic");
            Assert.Contains(r.Kits, k => k.Group == "archive/Old/Nest - Black" && k.Source == "Old.zip!Nest - Black/k.json");
            Assert.Equal(1, r.FilesSkipped);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void KitLinesChunkAndRepeat()
    {
        var kit = new Kit
        {
            Items =
            {
                new KitItem { Code = "Batteries", Amount = 23 },
                new KitItem { Code = "#spawnvehicle BP_SUV_01_A", Amount = 2 },
                new KitItem { SahId = 99, Amount = 1 },
                new KitItem { Code = "Weapon_M9", Amount = 10 },
            },
            ExtraCommands = { "#SetTime 12" },
        };
        var lines = SahImport.KitLines(kit, (c, n) => $"#SpawnItem {c} {n}", 10, out var unresolved);
        Assert.Equal(1, unresolved);
        Assert.Equal(new[]
        {
            "#SpawnItem Batteries 10", "#SpawnItem Batteries 10", "#SpawnItem Batteries 3",
            "#spawnvehicle BP_SUV_01_A", "#spawnvehicle BP_SUV_01_A",
            "#SpawnItem Weapon_M9 10",
            "#SetTime 12",
        }, lines);
    }

    [Fact]
    public void CloneIsDeepAndDetachedFromSource()
    {
        var kit = Import(Pack, "Hero - Soldier - SAH Pack (25-04-2026).json").Kits[0];
        kit.ExtraCommands.Add("#SetTime 12");
        var copy = kit.Clone(new[] { "Hero - Soldier", "Hero - Soldier (copy)" });

        Assert.Equal("Hero - Soldier (copy 2)", copy.Name);
        Assert.Equal(kit.Group, copy.Group);
        Assert.Equal("", copy.Source);
        Assert.Null(copy.SourceDate);
        Assert.Equal(kit.Items.Select(i => (i.Code, i.Amount, i.SahId)), copy.Items.Select(i => (i.Code, i.Amount, i.SahId)));

        copy.Items[0].Amount = 99;
        copy.Items.RemoveAt(1);
        copy.ExtraCommands.Clear();
        Assert.Equal(1, kit.Items[0].Amount);
        Assert.Equal(3, kit.Items.Count);
        Assert.Single(kit.ExtraCommands);
    }

    [Fact]
    public void ItemMapIgnoresJunk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sacc-map-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"1":"Banana","x":"Nope","2":"","3":"Two Words","4":5,"5":"#spawnvehicle BP_SUV_01_A","6":"#a\nb"}""");
            Assert.Equal(new Dictionary<int, string> { [1] = "Banana", [5] = "#spawnvehicle BP_SUV_01_A" }, SahImport.LoadItemMap(path));
            Assert.Empty(SahImport.LoadItemMap(path + ".missing"));
        }
        finally { File.Delete(path); }
    }
}
