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
    /// <summary>The checkbox in this category's group header (shared by every category in the section).</summary>
    public MapSectionToggle? SectionToggle { get; set; }

    /// <summary>Changes visibility without saving; the caller saves once for the whole batch.</summary>
    internal void SetQuietly(bool visible) { if (_visible != visible) { _visible = visible; Raise(nameof(IsVisible)); } }
}

/// <summary>A section's header checkbox: ticked when all its categories are shown, half-ticked when some are.</summary>
public sealed class MapSectionToggle : ObservableObject
{
    private readonly Action<IEnumerable<MapCategoryToggle>, bool> _setMany;

    public MapSectionToggle(string name, Action<IEnumerable<MapCategoryToggle>, bool> setMany)
    {
        Name = name;
        _setMany = setMany;
    }

    public string Name { get; }
    public List<MapCategoryToggle> Members { get; } = new();
    public bool? IsChecked { get => State(Members); set => _setMany(Members, ClickTurnsOn(Members, value)); }
    public void Refresh() => Raise(nameof(IsChecked));

    public static bool? State(IEnumerable<MapCategoryToggle> items)
    {
        bool any = false, all = true;
        foreach (var c in items) { any |= c.IsVisible; all &= c.IsVisible; }
        return all ? true : any ? null : false;
    }

    /// <summary>WPF turns a half-ticked box off when clicked; here it shows the rest instead.</summary>
    public static bool ClickTurnsOn(IEnumerable<MapCategoryToggle> items, bool? value) => value == true || State(items) == null;
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
    /// <summary>Asks the map to centre on a world point at a zoom level.</summary>
    public event Action<Vec3, double>? MapGoRequested;

    public IReadOnlyList<KeyValuePair<GridStyle, string>> MapGridChoices { get; } = new[]
    {
        new KeyValuePair<GridStyle, string>(GridStyle.Off, "No grid"),
        new KeyValuePair<GridStyle, string>(GridStyle.Sectors, "Sectors (B2)"),
        new KeyValuePair<GridStyle, string>(GridStyle.SectorsAndKeypads, "Sectors + keypad lines"),
        new KeyValuePair<GridStyle, string>(GridStyle.Keypads, "Keypads (B27)"),
        new KeyValuePair<GridStyle, string>(GridStyle.MapKeypads, "Map keypads 15×15 (A0 top-left)"),
        new KeyValuePair<GridStyle, string>(GridStyle.MapKeypadsInverted, "Map keypads 15×15 (inverted)"),
    };

    public GridStyle MapGrid
    {
        get => Settings.MapGrid;
        set { if (value == Settings.MapGrid) return; Settings.MapGrid = value; Raise(); Save(); MapChanged?.Invoke(); }
    }

    /// <summary>Sector maths for the loaded map, or the Island's known bounds before it loads.</summary>
    private SectorGrid Grid => _map == null ? SectorGrid.Island : new SectorGrid(new MapProjection(_map.Template.Settings));

    private string _mapGoTo = "";
    public string MapGoTo { get => _mapGoTo; set { if (Set(ref _mapGoTo, value)) CommandManager.InvalidateRequerySuggested(); } }

    private ICommand? _goTo;
    public ICommand MapGoToCommand => _goTo ??= new RelayCommand(GoTo, () => !string.IsNullOrWhiteSpace(MapGoTo));

    /// <summary>Go to "B2" (sector), "B27" (keypad), "X Y" coordinates, or a player by name or SteamID.</summary>
    private void GoTo()
    {
        var text = MapGoTo.Trim();
        var grid = Grid;
        if (grid.TryParse(text, out _, out var isKeypad) && grid.CenterOf(text) is { } centre)
        {
            MapGoRequested?.Invoke(centre, isKeypad ? 4.5 : 3);
            MapPointPicked(centre, null);
            Status = $"Centred on {text.ToUpperInvariant().Replace(" ", "")}.";
            return;
        }

        var nums = System.Text.RegularExpressions.Regex.Matches(text, @"-?\d+(?:\.\d+)?").Select(m => m.Value).ToList();
        if (nums.Count is 2 or 3 && !text.Any(char.IsLetter))
        {
            var p = new Vec3(double.Parse(nums[0], CultureInfo.InvariantCulture), double.Parse(nums[1], CultureInfo.InvariantCulture),
                nums.Count == 3 ? double.Parse(nums[2], CultureInfo.InvariantCulture) : Settings.MapTeleportZ);
            MapGoRequested?.Invoke(p, 4.5);
            MapPointPicked(p, null);
            Status = "Centred on those coordinates.";
            return;
        }

        var player = Players.FirstOrDefault(pl => pl.SteamId == text)
                     ?? Players.FirstOrDefault(pl => string.Equals(pl.Nickname, text, StringComparison.OrdinalIgnoreCase))
                     ?? Players.FirstOrDefault(pl => pl.Nickname.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (player?.Location is { } loc)
        {
            MapGoRequested?.Invoke(loc, 5);
            MapPointPicked(loc, null);
            Status = $"Centred on {player.Display} (last known position).";
            return;
        }
        Status = player != null
            ? $"{player.Display} has no known position. Refresh players first."
            : "Type a sector (B2), keypad (B27), coordinates (X Y) or a player name.";
    }

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

    private bool _mapShowOnline = true;
    /// <summary>Players in the last #ListPlayers output, at the position it gave.</summary>
    public bool MapShowOnline { get => _mapShowOnline; set { if (Set(ref _mapShowOnline, value)) MapChanged?.Invoke(); } }

    private bool _mapShowPlayers;
    /// <summary>Everyone else, at their last known position.</summary>
    public bool MapShowPlayers { get => _mapShowPlayers; set { if (Set(ref _mapShowPlayers, value)) MapChanged?.Invoke(); } }

    private bool _mapShowFlags;
    public bool MapShowFlags { get => _mapShowFlags; set { if (Set(ref _mapShowFlags, value)) MapChanged?.Invoke(); } }

    public DateTime? PlayersListedAt => _profile.PlayersListedAt;

    public string MapOnlineLabel => $"Online players ({Players.Count(p => PlayersListedAt is { } at && p.LastSeen >= at)})";
    public string MapOnlineTip => _profile.PlayersListedAt is { } at
        ? $"Players in the #ListPlayers output read {at:MMM d HH:mm}, where it placed them (green)"
        : "No #ListPlayers output read yet: press Refresh";

    public string MapFlagsLabel => $"Flags ({Flags.Count})";
    public string MapFlagsTip => _profile.FlagsListedAt is { } at
        ? $"Base flags from the #ListFlags output read {at:MMM d HH:mm}"
        : "No #ListFlags output read yet: press Refresh, or Paste output you copied";

    // ---- flags (#ListFlags)

    private ICommand? _refreshFlags, _pasteFlags;
    public ICommand RefreshFlagsCommand => _refreshFlags ??= new AsyncCommand(RefreshFlagsAsync);
    public ICommand PasteFlagsCommand => _pasteFlags ??= new RelayCommand(() =>
    {
        var text = GameLink.TryGetClipboardText();
        if (string.IsNullOrWhiteSpace(text)) { Status = "Clipboard is empty. Copy the #ListFlags output first."; return; }
        // Pasting adds to the list, so several pages can be pasted one after another.
        var parsed = Parsing.ParseFlagList(text);
        if (parsed.Count == 0) { Status = "No flag locations (X= Y= Z=) found in that text."; return; }
        var added = Parsing.MergeFlags(Flags, parsed);
        FlagsUpdated($"Read {parsed.Count} flag(s), {added} new; {Flags.Count} in total. Paste the next page, or Refresh to re-read them all.");
    });

    /// <summary>Upper bound on #ListFlags pages read by one Refresh.</summary>
    private const int MaxFlagPages = 30;

    /// <summary>#ListFlags is paged: read page 1, 2, … until a page has no new flags.</summary>
    private async Task RefreshFlagsAsync()
    {
        var def = _catalog.Find("list-flags-copy");
        if (def == null) { Status = "The command catalog has no 'list-flags-copy' entry."; return; }

        var all = new List<FlagRecord>();
        int page;
        for (page = 1; page <= MaxFlagPages; page++)
        {
            var r = TemplateRenderer.Render(def, new Dictionary<string, string?> { ["page"] = page.ToString(CultureInfo.InvariantCulture) });
            if (!r.Ok) { Status = $"{def.Name}: {string.Join("  ", r.Errors)}"; return; }
            var output = await CaptureAsync(r.Text);
            if (output == null) break;
            if (Parsing.MergeFlags(all, Parsing.ParseFlagList(output)) == 0) break;
        }
        if (all.Count == 0)
        {
            if (!Settings.DryRun) Status = "No flag locations found in the #ListFlags output.";
            return;
        }
        Flags.Clear();
        foreach (var f in all) Flags.Add(f);
        FlagsUpdated($"Read {all.Count} flag(s) from {page - 1} page(s).");
    }

    private void FlagsUpdated(string status)
    {
        _profile.FlagsListedAt = DateTime.Now;
        Raise(nameof(MapFlagsLabel));
        Raise(nameof(MapFlagsTip));
        MapShowFlags = true;
        Status = status;
        Save();
    }

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
            _mapSections.Clear();
            foreach (var c in _map.Categories.Where(c => counts.ContainsKey(c.Id)))
            {
                var toggle = new MapCategoryToggle(c, counts[c.Id], visible.Contains(c.Id), OnCategoryToggled);
                if (!_mapSections.TryGetValue(toggle.Section, out var section))
                    _mapSections[toggle.Section] = section = new MapSectionToggle(toggle.Section, SetCategories);
                section.Members.Add(toggle);
                toggle.SectionToggle = section;
                MapCategories.Add(toggle);
            }
            Raise(nameof(MapAllLayers));
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
        foreach (var s in _mapSections.Values) s.Refresh();
        Raise(nameof(MapAllLayers));
        RecomputeVisibleMarkers();
        Save();
    }

    private readonly Dictionary<string, MapSectionToggle> _mapSections = new();

    /// <summary>The "All layers" checkbox: half-ticked when some layers are shown.</summary>
    public bool? MapAllLayers
    {
        get => MapCategories.Count == 0 ? false : MapSectionToggle.State(MapCategories);
        set => SetCategories(MapCategories, MapSectionToggle.ClickTurnsOn(MapCategories, value));
    }

    private void SetCategories(IEnumerable<MapCategoryToggle> categories, bool on)
    {
        foreach (var c in categories) c.SetQuietly(on);
        OnCategoryToggled();
    }

    private void SetAllCategories(bool on) => SetCategories(MapCategories.Where(c => MapCategoriesView.PassesFilter(c)).ToList(), on);

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
        MapHover = Grid.KeypadName(w.X, w.Y) + FormattableString.Invariant($"   X {w.X:0}   Y {w.Y:0}") + (marker != null ? $"   ·   {marker.Title}" : "");
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
        UpdateGmSpot();
        var p = MapPoint.Value;
        MapPointText = (marker != null ? $"{marker.Title}: " : "")
                       + Grid.KeypadName(p.X, p.Y) + "   "
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
