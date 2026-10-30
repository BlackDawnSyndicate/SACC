using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Sacc.App.ViewModels;
using Sacc.Core.Model;

namespace Sacc.App.Infrastructure;

public sealed class ParamTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }
    public DataTemplate? Bool { get; set; }
    public DataTemplate? Choice { get; set; }
    public DataTemplate? Player { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (item as ParamFieldViewModel)?.Def.Kind switch
        {
            ParamKind.Bool => Bool,
            ParamKind.Choice => Choice,
            ParamKind.Player => Player,
            _ => Text,
        };
}

public sealed class ConfidenceBrushConverter : IValueConverter
{
    private static readonly Brush Documented = Frozen(0x2E, 0x7D, 0x32);
    private static readonly Brush Conflicting = Frozen(0xE0, 0x8A, 0x00);
    private static readonly Brush Unverified = Frozen(0x8E, 0x24, 0xAA);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Confidence.Documented => Documented,
        Confidence.Conflicting => Conflicting,
        _ => Unverified,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Keypad name ("B27") for a player's last position or a saved location.</summary>
public sealed class SectorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        PlayerRecord { Location: { } l } => Sacc.Core.SectorGrid.Island.KeypadName(l.X, l.Y),
        Bookmark b => Sacc.Core.SectorGrid.Island.KeypadName(b.X, b.Y),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 || value is not (string or null) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
