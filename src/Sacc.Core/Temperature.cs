namespace Sacc.Core;

/// <summary>
/// A rough average-temperature model for the island, from altitude alone. The game stores no temperature map, and
/// temperature falls with height, so this drops a sea-level temperature by a lapse rate per metre of altitude. It is a
/// guess for a heat overlay — as scum-map.com's own temperature map is — not the game's live temperature, which also
/// depends on weather, time of day and season. The constants are deliberately simple so they are easy to tune.
/// </summary>
public static class Temperature
{
    /// <summary>Average temperature at sea level (°C).</summary>
    public const double SeaLevelCelsius = 25.0;

    /// <summary>Drop in °C per metre of altitude (2.5 °C per 100 m — steeper than the real ~0.65, for SCUM's snowy peaks).</summary>
    public const double LapseCelsiusPerMetre = 0.025;

    /// <summary>The modelled average temperature (°C) at a ground height of <paramref name="heightCm"/> world cm; below sea level counts as sea level.</summary>
    public static double CelsiusAt(double heightCm) => SeaLevelCelsius - (LapseCelsiusPerMetre * Math.Max(0, heightCm) / 100.0);
}
