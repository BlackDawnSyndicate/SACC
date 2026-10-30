using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using Sacc.App.Game;
using Sacc.App.Infrastructure;
using Sacc.App.Map;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

public sealed class MapCategoryToggle : ObservableObject
{
    private readonly Action _changed;
    private bool _visible;

    public MapCategoryToggle(MapCategory category, int count, bool visible, Action changed)
    {
        Category = category;
        Count = count;
        _visible = visible;
        _changed = changed;
    }

    public MapCategory Category { get; }
    public int Count { get; }
    public string Section => Category.Section ?? "Other";
    public string Label => $"{Category.Name}  ({Count})";
    public bool IsVisible { get => _visible; set { if (Set(ref _visible, value)) _changed(); } }
}

/// <summary>The Map tab: scum-map.com layers (with their permission and attribution), players, saved locations, click-to-teleport.</summary>
public sealed partial class MainViewModel
{
    private readonly MapDataService _mapData = new();
    private MapDataService.MapData? _map;
    private bool _mapLoading;
    private ICommand? _refreshMap, _teleportHere, _copyTeleport, _saveHere, _openScumMap, _showAllCats, _hideAllCats;

    public MapDataService MapDataService => _mapData;
    public ObservableCollection<MapCategoryToggle> MapCategories { get; } = new();
    public ListCollectionView MapCategoriesView { get; private set; } = new(new List<MapCategoryToggle>());

    public event Action? MapChanged;

    public MapTemplate? MapTemplate => _map?.Template;
    public IReadOnlyDictionary<int, MapCategory> MapCategoryLookup { get; private set; } = new Dictionary<int, MapCategory>();
    public IReadOnlyList<MapMarker> VisibleMapMarkers { get; private set; } = Array.Empty<MapMarker>();
    public IReadOnlyList<string> MapTypeNames => _map?.Template.Types.Select(t => t.Name).ToList() ?? new List<string>();

    public string MapType
    {
        get => Settings.MapType;
        set { if (value == null || value == Settings.MapType) return; Settings.MapType = value; Raise(); Save(); MapChanged?.Invoke(); }
    }

    public string? ActiveTileTemplate =>
        (_map?.Template.Types.FirstOrDefault(t => t.Name == Settings.MapType) ?? _map?.Template.Types.FirstOrDefault())?.TemplateUrl;

    private string _mapStatus = "The map loads when you open this tab.";
    public string MapStatus { get => _mapStatus; private set => Set(ref _mapStatus, value); }

    private string _mapHover = "";
    public string MapHover { get => _mapHover; set => Set(ref _mapHover, value); }

    private string _mapTarget = "";
    /// <summary>Who "Teleport here" moves; empty = you (no player argument).</summary>
    public string MapTarget { get => _mapTarget; set => Set(ref _mapTarget, value); }

    private bool _mapShowPlayers = true;
    public bool MapShowPlayers { get => _mapShowPlayers; set { if (Set(ref _mapShowPlayers, value)) MapChanged?.Invoke(); } }

    private bool _mapShowBookmarks = true;
    public bool MapShowBookmarks { get => _mapShowBookmarks; set { if (Set(ref _mapShowBookmarks, value)) MapChanged?.Invoke(); } }

    /// <summary>The spot the last right-click (or click) was on, and the marker there if any.</summary>
    public Vec3? MapPoint { get; private set; }
    public MapMarker? MapPointMarker { get; private set; }

    private string _mapPointText = "Right-click the map for teleport and save options.";
    public string MapPointText { get => _mapPointText; private set => Set(ref _mapPointText, value); }

    public ICommand RefreshMapCommand => _refreshMap ??= new AsyncCommand(() => LoadMapAsync(force: true), () => !_mapLoading);
    public ICommand TeleportHereCommand => _teleportHere ??= new AsyncCommand(TeleportHereAsync, () => MapPoint != null);
    public ICommand CopyTeleportCommand => _copyTeleport ??= new RelayCommand(() =>
    {
        GameLink.SetClipboardText(MapProjection.TeleportLine(TeleportPoint(), MapTarget));
        Status = "Teleport command copied.";
    }, () => MapPoint != null);
    public ICommand SaveHereCommand => _saveHere ??= new RelayCommand(SaveHere, () => MapPoint != null);
    public ICommand OpenScumMapCommand => _openScumMap ??= new RelayCommand(o => OpenScumMap(o as string));
    public ICommand ShowAllMapCategoriesCommand => _showAllCats ??= new RelayCommand(() => SetAllCategories(true));
    public ICommand HideAllMapCategoriesCommand => _hideAllCats ??= new RelayCommand(() => SetAllCategories(false));

    /// <summary>Called when the Map tab is first shown.</summary>
    public Task EnsureMapLoadedAsync() => _map == null && !_mapLoading ? LoadMapAsync(force: false) : Task.CompletedTask;

    private async Task LoadMapAsync(bool force)
    {
        _mapLoading = true;
        CommandManager.InvalidateRequerySuggested();
        MapStatus = force ? "Refreshing map data from scum-map.com…" : "Loading map…";
        try
        {
            _map = await _mapData.LoadAsync(force, CancellationToken.None);
            MapCategoryLookup = _map.Categories.ToDictionary(c => c.Id);
            var counts = _map.Markers.GroupBy(m => m.CategoryId).ToDictionary(g => g.Key, g => g.Count());
            var visible = Settings.MapVisibleCategories.ToHashSet();
            MapCategories.Clear();
            foreach (var c in _map.Categories.Where(c => counts.ContainsKey(c.Id)))
                MapCategories.Add(new MapCategoryToggle(c, counts[c.Id], visible.Contains(c.Id), OnCategoryToggled));
            MapCategoriesView = new ListCollectionView(MapCategories);
            MapCategoriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MapCategoryToggle.Section)));
            Raise(nameof(MapCategoriesView));
            Raise(nameof(MapTypeNames));
            Raise(nameof(MapType));
            RecomputeVisibleMarkers();
            MapStatus = $"{_map.Markers.Count:N0} markers in {MapCategories.Count} categories · data "
                        + (_map.FromCache ? $"cached {_map.FetchedUtc.ToLocalTime():MMM d HH:mm}" : "just refreshed");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.InvalidDataException
                                       or System.Text.Json.JsonException or KeyNotFoundException or System.IO.IOException)
        {
            MapStatus = $"Couldn't load the map from scum-map.com: {ex.Message}";
            FileLog.Write("WARN", MapStatus);
        }
        finally
        {
            _mapLoading = false;
            CommandManager.InvalidateRequerySuggested();
            MapChanged?.Invoke();
        }
    }

    public void NoteWebpMissing() =>
        MapStatus = "Windows can't decode the map images (WebP). Install \"WebP Image Extensions\" from the Microsoft Store, then press Refresh.";

    private void OnCategoryToggled()
    {
        Settings.MapVisibleCategories = MapCategories.Where(c => c.IsVisible).Select(c => c.Category.Id).ToList();
        RecomputeVisibleMarkers();
        Save();
    }

    private void SetAllCategories(bool on)
    {
        foreach (var c in MapCategories.Where(c => MapCategoriesView.PassesFilter(c))) c.IsVisible = on;
    }

    private string _mapCategorySearch = "";
    public string MapCategorySearch
    {
        get => _mapCategorySearch;
        set
        {
            if (!Set(ref _mapCategorySearch, value)) return;
            MapCategoriesView.Filter = string.IsNullOrWhiteSpace(value) ? null
                : o => o is MapCategoryToggle t && (t.Category.Name.Contains(value, StringComparison.OrdinalIgnoreCase) || t.Section.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RecomputeVisibleMarkers()
    {
        var on = Settings.MapVisibleCategories.ToHashSet();
        VisibleMapMarkers = _map?.Markers.Where(m => on.Contains(m.CategoryId)).ToList() ?? new List<MapMarker>();
        MapChanged?.Invoke();
    }

    public void MapHovered(Vec3? world, MapMarker? marker)
    {
        if (world is not { } w) { MapHover = ""; return; }
        MapHover = FormattableString.Invariant($"X {w.X:0}   Y {w.Y:0}") + (marker != null ? $"   ·   {marker.Title}" : "");
    }

    /// <summary>Remember the clicked spot (snapping to a marker under the cursor) for the context actions.</summary>
    public void MapPointPicked(Vec3 world, MapMarker? marker)
    {
        if (marker != null && _map != null)
        {
            var w = new MapProjection(_map.Template.Settings).ToWorld(marker.MapX, marker.MapY, marker.Height ?? Settings.MapTeleportZ);
            MapPoint = w;
        }
        else MapPoint = world with { Z = Settings.MapTeleportZ };
        MapPointMarker = marker;
        var p = MapPoint.Value;
        MapPointText = (marker != null ? $"{marker.Title}: " : "")
                       + FormattableString.Invariant($"X {p.X:0.##}  Y {p.Y:0.##}  Z {p.Z:0.##}")
                       + (marker?.Height == null ? "  (Z unknown, using the setting)" : "");
        CommandManager.InvalidateRequerySuggested();
    }

    private Vec3 TeleportPoint() => MapPoint ?? default;

    private Task TeleportHereAsync()
    {
        var p = TeleportPoint();
        return RunCatalogAsync("teleport", new()
        {
            ["x"] = p.X.ToString("0.####", CultureInfo.InvariantCulture),
            ["y"] = p.Y.ToString("0.####", CultureInfo.InvariantCulture),
            ["z"] = p.Z.ToString("0.####", CultureInfo.InvariantCulture),
            ["player"] = MapTarget,
        });
    }

    private void SaveHere()
    {
        var p = TeleportPoint();
        var b = new Bookmark { Name = MapPointMarker?.Title ?? $"Map {DateTime.Now:HH:mm:ss}", X = p.X, Y = p.Y, Z = p.Z, Notes = "from the map" };
        Bookmarks.Add(b);
        Save();
        MapChanged?.Invoke();
        Status = $"Saved '{b.Name}' to Locations.";
    }

    /// <summary>Open scum-map.com, optionally centred on a world spot ("X,Y,zoom" is their own URL format).</summary>
    private void OpenScumMap(string? centre)
    {
        var url = string.IsNullOrEmpty(centre) ? ScumMapClient.SiteUrl : $"https://scum-map.com/en/scum/island/{centre}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
