using System.IO;
using Sacc.Core;

namespace Sacc.Core.Tests;

public class ItemNameTests
{
    [Theory]
    [InlineData("1H_Improvised_metal_knife", "One-handed Improvised Metal Knife")]
    [InlineData("Magazine_M16", "Magazine M16")]
    [InlineData("Hiking_Backpack_01_02", "Hiking Backpack")]   // trailing index groups dropped
    [InlineData("Weapon_AK47", "Weapon AK47")]
    [InlineData("BP_Wolf", "Wolf")]                              // blueprint prefix dropped
    [InlineData("Cotton_Shirt", "Cotton Shirt")]
    public void Prettify_makes_readable_labels(string code, string expected) =>
        Assert.Equal(expected, ItemName.Prettify(code));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Prettify_is_safe_on_empty(string? code) =>
        Assert.Equal("", ItemName.Prettify(code));

    [Fact]
    public void Prettify_keeps_a_lone_number()
    {
        // A pure-number first token has nothing before it to fold into, so it stays.
        Assert.Equal("05 Teeth Necklace", ItemName.Prettify("05_Teeth_Necklace"));
    }
}

public class ItemMetaTableTests
{
    [Fact]
    public void Missing_file_yields_empty_table()
    {
        var table = ItemMetaTable.Load(Path.Combine(Path.GetTempPath(), "sacc-no-such-meta.json"));
        Assert.Equal(0, table.Count);
        Assert.Null(table.Get("Anything"));
    }

    [Fact]
    public void Null_or_empty_path_yields_empty_table()
    {
        Assert.Same(ItemMetaTable.Empty, ItemMetaTable.Load(null));
        Assert.Same(ItemMetaTable.Empty, ItemMetaTable.Load(""));
    }

    [Fact]
    public void Loads_fields_and_looks_up_case_insensitively()
    {
        var json = """
        {
          "source": "test",
          "items": {
            "Magazine_M16": { "name": "M16 Magazine", "weightKg": 0.42, "width": 1, "height": 2,
                              "rarity": "Common", "traderBuy": 1200, "traderSell": 300, "fameToBuy": 5000, "dlc": null,
                              "description": "Holds 30 rounds." }
          }
        }
        """;
        var path = WriteTemp(json);
        try
        {
            var table = ItemMetaTable.Load(path);
            Assert.Equal(1, table.Count);
            var m = table.Get("magazine_m16");   // different case
            Assert.NotNull(m);
            Assert.Equal("Magazine_M16", m!.Code);
            Assert.Equal("M16 Magazine", m.Name);
            Assert.Equal(0.42, m.WeightKg);
            Assert.Equal(1, m.Width);
            Assert.Equal(2, m.Height);
            Assert.Equal("Common", m.Rarity);
            Assert.Equal(1200, m.TraderBuy);
            Assert.Equal(300, m.TraderSell);
            Assert.Equal(5000, m.FameToBuy);
            Assert.Null(m.Dlc);
            Assert.Equal("Holds 30 rounds.", m.Description);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Malformed_file_yields_empty_table_rather_than_throwing()
    {
        var path = WriteTemp("{ this is not valid json");
        try { Assert.Equal(0, ItemMetaTable.Load(path).Count); }
        finally { File.Delete(path); }
    }

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sacc-meta-{System.Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
