using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sacc.App.Infrastructure;

/// <summary>
/// Item thumbnails by spawn code. Looks in the user's icons folder first (their own
/// screenshots win), then the bundled set. Images are decoded small, frozen and cached,
/// misses included, so scrolling a 2,000-row list stays cheap.
/// </summary>
public static class IconCache
{
    private const int DecodeSize = 64;
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string UserFolder => Path.Combine(AppPaths.Root, "icons");
    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "data", "icons");

    public static void Clear() => Cache.Clear();

    public static int BundledCount =>
        Directory.Exists(BundledFolder) ? Directory.EnumerateFiles(BundledFolder, "*.png").Count() : 0;

    public static ImageSource? Get(string? code)
    {
        var name = FileNameFor(code);
        if (name == null) return null;
        if (Cache.TryGetValue(name, out var cached)) return cached;

        ImageSource? image = null;
        foreach (var folder in new[] { UserFolder, BundledFolder })
        {
            var path = Path.Combine(folder, name + ".png");
            if (!File.Exists(path)) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
                bmp.DecodePixelWidth = DecodeSize;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                image = bmp;
                break;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                FileLog.Write("WARN", $"Icon unreadable: {path}: {ex.Message}");
            }
        }
        Cache[name] = image;
        return image;
    }

    /// <summary>"#spawnvehicle BP_SUV_01_A" → "BP_SUV_01_A"; rejects anything that isn't a plain file name.</summary>
    public static string? FileNameFor(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var name = code.Trim();
        if (name.StartsWith('#')) name = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith('#') ? null : name;
    }
}

public sealed class CodeToIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => IconCache.Get(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
