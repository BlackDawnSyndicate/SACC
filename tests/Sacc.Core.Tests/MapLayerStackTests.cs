using Sacc.Core;

namespace Sacc.Core.Tests;

public class MapLayerStackTests
{
    private static readonly string[] Types = { "Regular", "Tactical" };

    [Fact]
    public void EmptySave_ShowsDefaultType_HidesRest_TemperatureOnTopHidden()
    {
        var r = MapLayers.Reconcile(null, Types, defaultVisibleType: "Regular");

        // Temperature rides on top (index 0), then the types in offered order.
        Assert.Equal(new[] { MapLayers.TemperatureId, "Regular", "Tactical" }, r.Select(l => l.Id));
        Assert.False(r[0].Visible);                       // temperature hidden by default
        Assert.True(r.Single(l => l.Id == "Regular").Visible);
        Assert.False(r.Single(l => l.Id == "Tactical").Visible);
    }

    [Fact]
    public void EmptySave_UnknownDefault_FallsBackToFirstType()
    {
        var r = MapLayers.Reconcile(null, Types, defaultVisibleType: "Nope");
        Assert.True(r.Single(l => l.Id == "Regular").Visible);   // first offered type
    }

    [Fact]
    public void EmptySave_HonoursTemperatureVisible()
    {
        var r = MapLayers.Reconcile(null, Types, "Regular", temperatureVisible: true);
        Assert.True(r.Single(l => l.Id == MapLayers.TemperatureId).Visible);
    }

    [Fact]
    public void KeepsSavedOrderVisibilityAndOpacity()
    {
        var saved = new List<MapLayerState>
        {
            new() { Id = "Tactical", Visible = true, Opacity = 0.4 },
            new() { Id = MapLayers.TemperatureId, Visible = true, Opacity = 0.7 },
            new() { Id = "Regular", Visible = false, Opacity = 1.0 },
        };

        var r = MapLayers.Reconcile(saved, Types);

        Assert.Equal(new[] { "Tactical", MapLayers.TemperatureId, "Regular" }, r.Select(l => l.Id));
        Assert.True(r[0].Visible);
        Assert.Equal(0.4, r[0].Opacity);
        Assert.Equal(0.7, r[1].Opacity);
    }

    [Fact]
    public void DropsTypesNoLongerOffered()
    {
        var saved = new List<MapLayerState> { new() { Id = "Satellite", Visible = true } };
        var r = MapLayers.Reconcile(saved, Types);
        Assert.DoesNotContain(r, l => l.Id == "Satellite");
        Assert.Contains(r, l => l.Id == "Regular");
        Assert.Contains(r, l => l.Id == "Tactical");
    }

    [Fact]
    public void AppendsNewlyOfferedTypesHidden()
    {
        var saved = new List<MapLayerState> { new() { Id = "Regular", Visible = true } };
        var r = MapLayers.Reconcile(saved, new[] { "Regular", "Tactical" });

        var tactical = r.Single(l => l.Id == "Tactical");
        Assert.False(tactical.Visible);                   // a new type doesn't switch itself on
        Assert.True(r.Single(l => l.Id == "Regular").Visible);
    }

    [Fact]
    public void AddsTemperatureOnTopWhenSaveHadNone()
    {
        var saved = new List<MapLayerState> { new() { Id = "Regular", Visible = true } };
        var r = MapLayers.Reconcile(saved, Types, temperatureVisible: true);

        Assert.Equal(MapLayers.TemperatureId, r[0].Id);   // inserted at the top
        Assert.True(r[0].Visible);
    }

    [Fact]
    public void TemperatureOnlySeed_ThenTypesArrive_StillDefaultsBaseVisible()
    {
        // First reconcile happens before the map's type list is known, leaving a temperature-only save.
        var seed = MapLayers.Reconcile(null, Array.Empty<string>(), "Regular");
        Assert.Equal(new[] { MapLayers.TemperatureId }, seed.Select(l => l.Id));

        // When the real types load, the default base layer should still switch on (not be treated as "new/hidden").
        var r = MapLayers.Reconcile(seed, Types, "Regular");
        Assert.True(r.Single(l => l.Id == "Regular").Visible);
        Assert.False(r.Single(l => l.Id == "Tactical").Visible);
    }

    [Fact]
    public void ClampsOpacityIntoRange()
    {
        var saved = new List<MapLayerState>
        {
            new() { Id = "Regular", Opacity = 5.0 },
            new() { Id = "Tactical", Opacity = -1.0 },
        };
        var r = MapLayers.Reconcile(saved, Types);
        Assert.Equal(1.0, r.Single(l => l.Id == "Regular").Opacity);
        Assert.Equal(MapLayers.MinOpacity, r.Single(l => l.Id == "Tactical").Opacity);
    }

    [Fact]
    public void DeduplicatesRepeatedSavedIds()
    {
        var saved = new List<MapLayerState>
        {
            new() { Id = "Regular", Visible = true },
            new() { Id = "Regular", Visible = false },
        };
        var r = MapLayers.Reconcile(saved, Types);
        Assert.Single(r, l => l.Id == "Regular");
        Assert.True(r.Single(l => l.Id == "Regular").Visible);   // the first wins
    }
}
