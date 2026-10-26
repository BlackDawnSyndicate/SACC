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
