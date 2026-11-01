using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.Core.Tests;

public class ItemCatalogTests
{
    private static readonly string[] Table =
    {
        "Magazine_M16", "Magazine_AK47", "Weapon_M16", "Magnifying_Glass", "M1_Clip", "Banana",
        "#spawnvehicle BP_SUV_01_A", "#spawnzombie BP_Zombie_1",
    };

    [Fact]
    public void SplitsItemsAndVehiclesAndMergesExtras()
    {
        var c = new ItemCatalog(Table, extraItems: new[] { "NewPatchItem", "banana", "#Nope", "two words" }, extraVehicles: new[] { "BP_Bike" });
        Assert.Equal(new[] { "Banana", "M1_Clip", "Magazine_AK47", "Magazine_M16", "Magnifying_Glass", "NewPatchItem", "Weapon_M16" }, c.Items);
        Assert.Equal(new[] { "BP_Bike", "BP_SUV_01_A" }, c.Vehicles);
    }

    [Fact]
    public void FilterMatchesAllWordsAndRanksPrefixFirst()
    {
        var c = new ItemCatalog(Table);
        Assert.Equal(new[] { "Magazine_M16" }, ItemCatalog.Filter(c.Items, "m16 mag"));
        // Prefix matches ("M...") come before codes that only contain the text ("Weapon_M16").
        Assert.Equal(new[] { "M1_Clip", "Magazine_AK47", "Magazine_M16", "Magnifying_Glass", "Weapon_M16" }, ItemCatalog.Filter(c.Items, "m"));
        Assert.Equal("Weapon_M16", ItemCatalog.Filter(c.Items, "weapon_m16")[0]);
        Assert.Equal(c.Items.Count, ItemCatalog.Filter(c.Items, "  ").Count);
        Assert.Equal(2, ItemCatalog.Count(c.Items, "m16"));
    }

    [Fact]
    public void GameListSpellingWinsAndUnlistedSortLast()
    {
        var game = new[] { "magazine_m16", "Weapon_M16", "Animal_skin", "Apex_Jacket_01" };
        var c = new ItemCatalog(Table.Append("Animal_Skin").Append(""), extraItems: new[] { "Bikers_Logo_Jacket_01" }, gameItems: game);
        Assert.Equal(new[] { "Animal_skin", "Apex_Jacket_01", "magazine_m16", "Weapon_M16",
                             "Banana", "Bikers_Logo_Jacket_01", "M1_Clip", "Magazine_AK47", "Magnifying_Glass" }, c.Items);
        Assert.True(c.IsUnlisted("Banana"));
        Assert.True(c.IsUnlisted("bikers_logo_jacket_01"));
        Assert.False(c.IsUnlisted("Animal_Skin"));
        // Within a rank, codes the game lists come first.
        Assert.Equal(new[] { "magazine_m16", "Magazine_AK47", "Magnifying_Glass" }, ItemCatalog.Filter(c.Items, "mag", unlisted: c.Unlisted));
    }

    [Fact]
    public void NoGameListMeansNothingUnlisted() => Assert.Empty(new ItemCatalog(Table).Unlisted);

    [Fact]
    public void GameItemList_ReadsFullListsFromTheClientLog()
    {
        var log = new System.Text.StringBuilder();
        void Line(string s) => log.Append($"[2026.11.01-15.52.31:034][879]LogSCUM: {s}\r\n");
        // A filtered search, then a full list (entries are indented, chat echoes have one space).
        Line("START OF 'Item' LIST"); Line("    Magazine_M16"); Line("END OF 'Item' LIST");
        Line("START OF 'Item' LIST");
        for (int i = 0; i < GameItemList.MinFullList; i++) { Line($"    Item_{i:D4}"); Line($"Sender player state null for message Item_{i:D4}!"); }
        Line("END OF 'Item' LIST");
        Line("START OF 'Vehicle' LIST"); Line("    BPC_Rager"); Line("END OF 'Vehicle' LIST");
        log.Append("[2026.11.01-15.53.00:000][1]LogSCUM: START OF 'Item' LIST\r\n[x]LogSCUM:     Unfinished\r\n");

        var lists = GameItemList.ParseLists(log.ToString());
        Assert.Equal(new[] { 1, GameItemList.MinFullList }, lists.Select(l => l.Count));
        var full = GameItemList.LastFullList(log.ToString())!;
        Assert.Equal("Item_0000", full[0]);
        Assert.DoesNotContain(full, i => i.StartsWith("Sender"));
        Assert.Null(GameItemList.LastFullList("nothing here"));
    }

    [Fact]
    public void MagazineMatcher()
    {
        var isMag = ItemCatalog.MagazineMatcher(new Settings().MagazinePattern);
        Assert.True(isMag("Magazine_M16"));
        Assert.True(isMag("M1_Clip"));
        Assert.False(isMag("Magnifying_Glass"));
        Assert.False(isMag("Shemagh_Scarf_01"));
        Assert.False(ItemCatalog.MagazineMatcher("(unclosed")("Magazine_M16"));
    }

    [Fact]
    public void UniqueKitNames()
    {
        Assert.Equal("New kit", Kit.UniqueName("New kit", new[] { "Other" }));
        Assert.Equal("New kit 3", Kit.UniqueName("New kit", new[] { "new kit", "New kit 2" }));
    }

    [Fact]
    public void AddItemMergesOnlyIdenticalRows()
    {
        var kit = new Kit();
        var a = kit.AddItem("Magazine_M16", 2, null, stackFull: true);
        Assert.Same(a, kit.AddItem("magazine_m16", 3, 30, stackFull: true)); // Full ignores the number
        Assert.Equal(5, a.Amount);
        Assert.Null(a.StackCount);

        kit.AddItem("Magazine_M16", 1, 30, false);     // different stack → new row
        kit.AddItem("Magazine_M16", 1, 30, false);     // same → merged
        kit.AddItem("Batteries", 0, 0, false);         // amount floors at 1, stack 0 = none
        Assert.Equal(3, kit.Items.Count);
        Assert.Equal(2, kit.Items[1].Amount);
        Assert.Equal((1, (int?)null), (kit.Items[2].Amount, kit.Items[2].StackCount));
    }

    [Fact]
    public void KitFillMagazinesOnlyTouchesMagazines()
    {
        var kit = new Kit
        {
            FillMagazines = true,
            Items =
            {
                new KitItem { Code = "Magazine_M16", Amount = 3 },
                new KitItem { Code = "Magazine_AK47", Amount = 1, StackCount = 10 },
                new KitItem { Code = "Weapon_M16", Amount = 1 },
            },
        };
        var isMag = ItemCatalog.MagazineMatcher(new Settings().MagazinePattern);
        string R(string c, int n, int? s) => s is { } v ? $"{c} {n} S{v}" : $"{c} {n}";

        Assert.Equal(new[] { "Magazine_M16 3 S200", "Magazine_AK47 1 S200", "Weapon_M16 1" },
            SahImport.KitLines(kit, R, 10, 200, out _, isMag));

        kit.FillMagazines = false;
        Assert.Equal(new[] { "Magazine_M16 3", "Magazine_AK47 1 S10", "Weapon_M16 1" },
            SahImport.KitLines(kit, R, 10, 200, out _, isMag));
    }
}
