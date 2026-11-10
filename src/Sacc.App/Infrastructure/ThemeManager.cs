using System.Windows;
using System.Windows.Media;

namespace Sacc.App.Infrastructure;

/// <summary>
/// Colour schemes for SACC. The app keeps its light base (dark text on light panels); each scheme restyles the header
/// bar, the accent (the "SACC" title, section headings and the primary button) and small supporting tones. The six
/// brushes live in App.xaml as shared, mutable resources, so changing a brush's <see cref="SolidColorBrush.Color"/>
/// re-themes every control that uses it live, without a restart.
/// </summary>
public static class ThemeManager
{
    public sealed record Theme(string Name, string Header, string Accent, string Panel, string Muted);

    public const string Default = "Default";

    /// <summary>Header bar, accent, window/panel tint and muted-text colour per scheme (all keep dark text on a light body).</summary>
    public static readonly IReadOnlyList<Theme> Themes = new[]
    {
        new Theme(Default,   "#1F2A30", "#C8641E", "#F4F5F6", "#6B7780"),
        new Theme("Urban",   "#2B2F33", "#4E7A99", "#F0F1F2", "#66707A"),
        new Theme("Desert",  "#5E4E33", "#B8860B", "#F6F2E9", "#8A7A5A"),
        new Theme("Woodland","#2F3A26", "#4F7A2E", "#EEF2E8", "#5E6E4E"),
        new Theme("Multicam","#45412F", "#7E6E42", "#F3F1E8", "#6E6650"),
        new Theme("Night",   "#121724", "#4A7DD4", "#ECEEF3", "#5B6472"),
        new Theme("Arctic",  "#39515F", "#1F93BC", "#EDF4F7", "#5E6E78"),
        new Theme("Raider",  "#1C1C1C", "#C0392B", "#F3F0F0", "#7A6A6A"),
        new Theme("Clown",   "#6A2EA0", "#E0219E", "#F6ECF4", "#8A6A9E"),
        new Theme("Punk",    "#151515", "#E6007E", "#F5ECF1", "#7A5A6E"),
        new Theme("Sunset",  "#6E342A", "#E8662E", "#FAF1EA", "#9A6E5E"),
    };

    public static IEnumerable<string> Names => Themes.Select(t => t.Name);

    /// <summary>Applies the named scheme to the live resource brushes (falls back to the default for an unknown name).</summary>
    public static void Apply(string? name)
    {
        var theme = Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
        if (Application.Current?.Resources is not { } res)
        {
            return;
        }

        Set(res, "HeaderBrush", theme.Header);
        Set(res, "AccentBrush", theme.Accent);
        Set(res, "PanelBrush", theme.Panel);
        Set(res, "MutedBrush", theme.Muted);
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        if (res[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
