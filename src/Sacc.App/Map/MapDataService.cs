using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Sacc.App.Infrastructure;
using Sacc.Core;

namespace Sacc.App.Map;

/// <summary>
/// Map data and tiles from scum-map.com, cached on disk. Their team no longer maintains the
/// service, so SACC is deliberately gentle: template/categories/markers are re-fetched at most
/// once a day (or on Refresh), tiles are downloaded once and kept, and at most a few tile
/// requests run at a time. When offline, the last cached copy is used.
/// </summary>
public sealed class MapDataService
{
    private static readonly TimeSpan DataMaxAge = TimeSpan.FromDays(1);
    private const int MemoryTiles = 160;

    private readonly HttpClient _http;
    private readonly ScumMapClient _client;
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly Dictionary<string, Task<BitmapSource?>> _inFlight = new();
    private readonly Dictionary<string, BitmapSource?> _memory = new();
    private readonly LinkedList<string> _lru = new();

    public MapDataService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var version = typeof(MapDataService).Assembly.GetName().Version?.ToString(3) ?? "0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"SACC/{version} (SCUM Admin Command Console; displays scum-map.com layers with permission)");
        _client = new ScumMapClient(_http);
    }

    public static string CacheRoot => Path.Combine(AppPaths.Root, "mapcache");

    public sealed record MapData(MapTemplate Template, IReadOnlyList<MapCategory> Categories, IReadOnlyList<MapMarker> Markers, DateTime FetchedUtc, bool FromCache);

    /// <summary>Load map data; uses the disk cache unless it's stale or <paramref name="forceRefresh"/>.</summary>
    public async Task<MapData> LoadAsync(bool forceRefresh, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheRoot);
        var templatePath = Path.Combine(CacheRoot, "template.json");
        var categoriesPath = Path.Combine(CacheRoot, "categories.json");
        var markersPath = Path.Combine(CacheRoot, "markers.json");

        var cacheComplete = File.Exists(templatePath) && File.Exists(categoriesPath) && File.Exists(markersPath);
        var fresh = cacheComplete && DateTime.UtcNow - File.GetLastWriteTimeUtc(markersPath) < DataMaxAge;

        if (!forceRefresh && fresh) return FromDisk(templatePath, categoriesPath, markersPath, fromCache: true);
        try
        {
            var templateJson = await _client.TemplateJsonAsync("scum", "island", ct);
            var template = ScumMapClient.ParseTemplate(templateJson);
            var categoriesJson = await _client.CategoriesJsonAsync(template.Id, ct);
            var markersJson = await _client.MarkersJsonAsync(template.Id, ct);
            // Parse before saving so a bad response never replaces a good cache.
            ScumMapClient.ParseCategories(categoriesJson);
            ScumMapClient.ParseMarkers(markersJson);
            await File.WriteAllTextAsync(templatePath, templateJson, ct);
            await File.WriteAllTextAsync(categoriesPath, categoriesJson, ct);
            await File.WriteAllTextAsync(markersPath, markersJson, ct);
            return FromDisk(templatePath, categoriesPath, markersPath, fromCache: false);
        }
        catch (Exception ex) when (cacheComplete && ex is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            FileLog.Write("WARN", $"Map refresh failed, using cached map data: {ex.Message}");
            return FromDisk(templatePath, categoriesPath, markersPath, fromCache: true);
        }
    }

    private static MapData FromDisk(string t, string c, string m, bool fromCache) => new(
        ScumMapClient.ParseTemplate(File.ReadAllText(t)),
        ScumMapClient.ParseCategories(File.ReadAllText(c)),
        ScumMapClient.ParseMarkers(File.ReadAllText(m)),
        File.GetLastWriteTimeUtc(m), fromCache);

    /// <summary>Tile image, from memory, disk, or the network (null if unavailable).</summary>
    public Task<BitmapSource?> GetTileAsync(string template, int z, int x, int y)
    {
        var key = $"{template}|{z}|{x}|{y}";
        lock (_memory)
        {
            if (_memory.TryGetValue(key, out var hit)) { Touch(key); return Task.FromResult(hit); }
            if (_inFlight.TryGetValue(key, out var pending)) return pending;
            var task = LoadTileAsync(template, z, x, y, key);
            _inFlight[key] = task;
            return task;
        }
    }

    private async Task<BitmapSource?> LoadTileAsync(string template, int z, int x, int y, string key)
    {
        BitmapSource? image = null;
        try
        {
            var path = TilePath(template, z, x, y);
            if (!File.Exists(path))
            {
                await _downloads.WaitAsync();
                try
                {
                    if (!File.Exists(path))
                    {
                        var bytes = await _http.GetByteArrayAsync(ScumMapClient.TileUrl(template, z, x, y));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path + ".tmp", bytes);
                        File.Move(path + ".tmp", path, overwrite: true);
                    }
                }
                finally { _downloads.Release(); }
            }
            // Frozen bitmaps can cross threads; decoding off the UI thread keeps panning smooth.
            image = await Task.Run(() => Decode(path));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            FileLog.Write("WARN", $"Map tile {z}/{x}_{y} unavailable: {ex.Message}");
        }
        lock (_memory)
        {
            _inFlight.Remove(key);
            _memory[key] = image;
            Touch(key);
            while (_lru.Count > MemoryTiles) { _memory.Remove(_lru.Last!.Value); _lru.RemoveLast(); }
        }
        return image;
    }

    /// <summary>Set when Windows can't decode WebP (no "WebP Image Extensions" installed).</summary>
    public static bool WebpUnsupported { get; private set; }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            WebpUnsupported = true;
            FileLog.Write("WARN", $"Can't decode map tile {path}: {ex.Message}");
            return null;
        }
    }

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    /// <summary>Tiles are stored per template URL (which carries the map version), so a game patch starts a fresh set.</summary>
    private static string TilePath(string template, int z, int x, int y)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template)))[..16];
        return Path.Combine(CacheRoot, "tiles", hash, z.ToString(), $"{x}_{y}.webp");
    }
}
