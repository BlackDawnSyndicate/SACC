using System.Windows;
using System.Windows.Media;

namespace Sacc.App.Infrastructure;

/// <summary>
/// Colour schemes for SACC. Each scheme sets the header bar, the accent (the "SACC" title, section headings and the
/// primary button), the body/panel colour, the default text colour, the input (field) colour and the muted-text colour.
/// Light schemes use dark text on a tinted panel; the dark schemes (Night, Sunset, Raider, Punk) use light text on a dark
/// panel. The brushes live in App.xaml as shared, mutable resources, so changing a brush's
/// <see cref="SolidColorBrush.Color"/> re-themes every control that uses it live, without a restart. A few native-control
/// bits (combo-box drop-downs, button hover) keep the system chrome.
/// </summary>
public static class ThemeManager
{
    public sealed record Theme(string Name, string Header, string Accent, string Panel, string Muted, string Text, string Field, string Border);

    public const string Default = "Default";

    //                       Name        Header     Accent     Panel      Muted      Text       Field      Border
    public static readonly IReadOnlyList<Theme> Themes = new[]
    {
        new Theme(Default,   "#1F2A30", "#C8641E", "#F4F5F6", "#6B7780", "#23292E", "#FFFFFF", "#D7DCE0"),
        new Theme("Urban",   "#2B2F33", "#6FB0D6", "#5A6670", "#AEB8C0", "#EDF0F2", "#4C5862", "#6E7A84"),
        new Theme("Desert",  "#5E4E33", "#9A6E12", "#E8DCC2", "#786A4A", "#3A3020", "#F4ECD6", "#D2C4A4"),
        new Theme("Woodland","#2F3A26", "#8FC04E", "#4E5E3C", "#AEBC98", "#EEF2E6", "#42502F", "#637552"),
        new Theme("Multicam","#4A4A34", "#6E8240", "#DED8BC", "#6E6A4A", "#32301E", "#ECE9D6", "#C8C0A0"),
        new Theme("Arctic",  "#39515F", "#1683A8", "#D4E6EF", "#556673", "#1E2E38", "#ECF5FA", "#B6CEDA"),
        new Theme("Clown",   "#6A2EA0", "#D81E8E", "#F0DAEE", "#845A96", "#35153E", "#FBEDF9", "#DCBCD6"),
        new Theme("Night",   "#0C1018", "#5A90E0", "#141A28", "#8089A0", "#E6EAF2", "#1E2638", "#2A3242"),
        new Theme("Sunset",  "#1E1216", "#F2743B", "#291A1E", "#B5897C", "#F6E6DC", "#37242A", "#43302F"),
        new Theme("Raider",  "#141010", "#E24B3E", "#1E1414", "#9A7C7C", "#F0E2E2", "#2A1C1C", "#352626"),
        new Theme("Punk",    "#0F0A10", "#FF1E90", "#18131A", "#A484A4", "#F2E4EE", "#241D28", "#30262E"),
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
        Set(res, "TextBrush", theme.Text);
        Set(res, "FieldBrush", theme.Field);
        Set(res, "BorderBrush", theme.Border);
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        if (res[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
