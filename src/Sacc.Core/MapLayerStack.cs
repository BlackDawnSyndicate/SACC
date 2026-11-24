namespace Sacc.Core;

/// <summary>
/// One base map layer in the Map tab's stack: either a scum-map tile type (by its <see cref="MapTypeInfo.Name"/>)
/// or the altitude temperature overlay (<see cref="MapLayers.TemperatureId"/>). The list is ordered
/// top-to-bottom: index 0 is drawn on top, the last entry is the bottom of the stack. Persisted in Settings.
/// </summary>
public sealed class MapLayerState
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; }
    public double Opacity { get; set; } = 1.0;
}

/// <summary>Reconciles the persisted base-layer stack with the tile types the map actually offers right now.</summary>
public static class MapLayers
{
    /// <summary>Sentinel id for the temperature overlay layer. The leading control char can't collide with a scum-map type name.</summary>
    public const string TemperatureId = "\u0001temperature";

    public const double MinOpacity = 0.05;

    public static double ClampOpacity(double o) => double.IsNaN(o) ? 1.0 : Math.Clamp(o, MinOpacity, 1.0);

    /// <summary>
    /// Merge <paramref name="saved"/> with the available <paramref name="typeNames"/> plus a temperature layer:
    /// saved entries keep their order, visibility and opacity; types no longer offered are dropped; newly offered
    /// types are appended hidden; a temperature layer is added on top if the save had none. On an empty save the
    /// stack shows <paramref name="defaultVisibleType"/> (or the first type) and hides everything else.
    /// Returns a fresh list in top-to-bottom draw order.
    /// </summary>
    public static List<MapLayerState> Reconcile(
        IReadOnlyList<MapLayerState>? saved,
        IReadOnlyList<string> typeNames,
        string? defaultVisibleType = null,
        bool temperatureVisible = false)
    {
        saved ??= Array.Empty<MapLayerState>();
        var available = typeNames.Where(n => !string.IsNullOrEmpty(n) && n != TemperatureId)
                                 .Distinct(StringComparer.Ordinal).ToList();
        var result = new List<MapLayerState>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Keep saved entries that still exist (a known type, or the temperature layer), in their saved order.
        foreach (var s in saved)
        {
            var isTemp = s.Id == TemperatureId;
            if (!isTemp && !available.Contains(s.Id)) continue;
            if (!seen.Add(s.Id)) continue;
            result.Add(new MapLayerState { Id = s.Id, Visible = s.Visible, Opacity = ClampOpacity(s.Opacity) });
        }

        // "First run" = the user has never had a base tile layer configured. A temperature-only seed (written
        // before the map's type list is known) doesn't count, so the real types still default-on when they arrive.
        var firstRun = !saved.Any(s => s.Id != TemperatureId);
        var defaultName = defaultVisibleType != null && available.Contains(defaultVisibleType)
            ? defaultVisibleType
            : available.FirstOrDefault();

        // Append any newly offered type, hidden (except the default one on a first run).
        foreach (var name in available)
        {
            if (!seen.Add(name)) continue;
            result.Add(new MapLayerState { Id = name, Visible = firstRun && name == defaultName, Opacity = 1.0 });
        }

        // Temperature rides on top of the base tiles by default (index 0) when the save had none.
        if (seen.Add(TemperatureId))
            result.Insert(0, new MapLayerState { Id = TemperatureId, Visible = temperatureVisible, Opacity = 1.0 });

        return result;
    }
}
