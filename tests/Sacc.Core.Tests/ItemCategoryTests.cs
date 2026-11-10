using Sacc.Core;

namespace Sacc.Core.Tests;

public class ItemCategoryTests
{
    [Theory]
    [InlineData("Magazine_M16", "Magazines")]      // magazine beats the weapon keyword in the same code
    [InlineData("12_Gauge_Buckshot", "Ammunition")]
    [InlineData("12_Gauge_Buckshot_Ammobox", "Ammunition")]
    [InlineData("Weapon_AK47", "Weapons")]
    [InlineData("Combat_Knife", "Melee")]
    [InlineData("Hiking_Backpack_01", "Backpacks")]
    [InlineData("Cotton_Shirt", "Clothing")]
    [InlineData("Canned_Beans", "Food")]
    [InlineData("ColorCan_DarkGrey", "Resources")]   // paint cans must not land in Food via "can"
    [InlineData("ColorCan_FireEngineRed", "Resources")]
    [InlineData("Bottle_Of_Water", "Drinks")]
    [InlineData("Bandage", "Medical")]
    [InlineData("AA_Battery", "Electronics")]
    [InlineData("Car_Battery", "Vehicle Parts")]      // a car battery is a vehicle part, not generic electronics
    [InlineData("Car_Wheel", "Vehicle Parts")]
    [InlineData("Whiskey_Bottle", "Alcohol")]         // alcohol is its own category, ahead of Drinks
    [InlineData("Red_Wine", "Alcohol")]
    [InlineData("Bottle_Of_Soda", "Drinks")]
    [InlineData("Totally_Made_Up_Thing", "Other")]
    public void ClassifiesItemsByKeyword(string code, string expected) =>
        Assert.Equal(expected, ItemCategory.OfItem(code));

    [Theory]
    [InlineData("BPC_Bicycle_Mountain_Bike_Green", "Bicycles")]
    [InlineData("BPC_Dinghy", "Boats")]
    [InlineData("BPC_Laika", "Cars")]
    [InlineData("BPC_Mystery", "Other")]
    public void ClassifiesVehiclesByKeyword(string code, string expected) =>
        Assert.Equal(expected, ItemCategory.OfVehicle(code));

    [Fact]
    public void CountsAreInCanonicalOrderWithOtherLast()
    {
        var codes = new[] { "Weapon_AK47", "Magazine_M16", "Totally_Made_Up", "Combat_Knife", "Weapon_M4" };
        var counts = ItemCategory.Counts(codes);
        // Magazines precedes Weapons precedes Melee in the rule order; Other is last.
        Assert.Equal(new[] { "Magazines", "Weapons", "Melee", "Other" }, counts.Select(c => c.Name).ToArray());
        Assert.Equal(2, counts.Single(c => c.Name == "Weapons").Count);
        Assert.Equal("Other", counts[^1].Name);
    }

    [Fact]
    public void InCategoryKeepsEverythingForAllAndFiltersOtherwise()
    {
        var codes = new[] { "Weapon_AK47", "Magazine_M16", "Combat_Knife" };
        Assert.Equal(codes, ItemCategory.InCategory(codes, ItemCategory.All));
        Assert.Equal(codes, ItemCategory.InCategory(codes, null));
        Assert.Equal(new[] { "Magazine_M16" }, ItemCategory.InCategory(codes, "Magazines"));
    }
}
