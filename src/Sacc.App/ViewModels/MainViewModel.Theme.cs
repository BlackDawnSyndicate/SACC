using Sacc.App.Infrastructure;

namespace Sacc.App.ViewModels;

/// <summary>The colour-scheme picker (Settings tab); applies a theme to the live brushes.</summary>
public sealed partial class MainViewModel
{
    public IReadOnlyList<string> ThemeNames { get; } = ThemeManager.Names.ToList();

    public string Theme
    {
        get => Settings.Theme;
        set
        {
            if (string.Equals(value, Settings.Theme, StringComparison.Ordinal)) return;
            Settings.Theme = string.IsNullOrWhiteSpace(value) ? ThemeManager.Default : value;
            ThemeManager.Apply(Settings.Theme);
            Raise();
            Save();
        }
    }
}
