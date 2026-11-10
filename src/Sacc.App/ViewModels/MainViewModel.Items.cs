using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Sacc.App.Infrastructure;
using Sacc.Core;

namespace Sacc.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---- the Spawner's "To spawn" basket: double-clicking an item adds it here with the current settings,
    // then "Spawn all" sends every queued line in one batch (ExecuteAsync splits oversized counts).

    public ObservableCollection<SpawnQueueItem> SpawnQueue { get; } = new();

    private ICommand? _addToSpawnQueue, _removeSpawnQueueItem, _clearSpawnQueue, _spawnQueue;

    public ICommand AddToSpawnQueueCommand => _addToSpawnQueue ??= new RelayCommand(o =>
    {
        var code = (o as string)?.Trim();
        if (string.IsNullOrEmpty(code)) return;
        SpawnQueue.Add(new SpawnQueueItem { Code = code, Amount = ItemAmount, Stack = SpawnerStack(code) });
        CommandManager.InvalidateRequerySuggested();
        Status = $"Added {code} to the spawn list ({SpawnQueue.Count}).";
    });

    public ICommand RemoveSpawnQueueItemCommand => _removeSpawnQueueItem ??= new RelayCommand(o =>
    {
        if (o is SpawnQueueItem it) { SpawnQueue.Remove(it); CommandManager.InvalidateRequerySuggested(); }
    });

    public ICommand ClearSpawnQueueCommand => _clearSpawnQueue ??= new RelayCommand(
        () => { SpawnQueue.Clear(); CommandManager.InvalidateRequerySuggested(); },
        () => SpawnQueue.Count > 0);

    public ICommand SpawnQueueCommand => _spawnQueue ??= new AsyncCommand(SpawnQueueAsync, () => SpawnQueue.Count > 0);

    private async Task SpawnQueueAsync()
    {
        var def = _catalog.Find("spawn-item");
        if (def == null) { Status = "The command catalog has no 'spawn-item' entry."; return; }
        var lines = new List<string>();
        foreach (var e in SpawnQueue)
        {
            var r = TemplateRenderer.Render(def, new Dictionary<string, string?> { ["item"] = e.Code, ["amount"] = e.Amount, ["stack"] = e.Stack });
            if (r.Ok) lines.Add(r.Text);
            else { Status = $"{e.Code}: {string.Join("  ", r.Errors)}"; return; }
        }
        if (lines.Count == 0) { Status = "The spawn list is empty."; return; }
        await ExecuteAsync(lines);
    }

    private ICommand? _openIconsFolder, _reloadIcons;

    public ICommand OpenIconsFolderCommand => _openIconsFolder ??= new RelayCommand(() =>
    {
        Directory.CreateDirectory(IconCache.UserFolder);
        Process.Start(new ProcessStartInfo(IconCache.UserFolder) { UseShellExecute = true });
        Status = "Name your screenshots <SpawnCode>.png (e.g. Magazine_M16.png), then press Reload icons.";
    });

    public ICommand ReloadIconsCommand => _reloadIcons ??= new RelayCommand(() =>
    {
        IconCache.Clear();
        // New list instances make the bound lists re-render their rows, picking up changed images.
        ItemMatches = ItemMatches.ToList();
        VehicleMatches = VehicleMatches.ToList();
        RefreshKitItems();
        var user = Directory.Exists(IconCache.UserFolder) ? Directory.EnumerateFiles(IconCache.UserFolder, "*.png").Count() : 0;
        Status = $"Icons reloaded: {IconCache.BundledCount} bundled, {user} of your own.";
    });

    private const int MaxShownMatches = 300;
    private ItemCatalog _itemCatalog = new(Array.Empty<string>());

    private IReadOnlyList<string> _itemMatches = Array.Empty<string>();
    public IReadOnlyList<string> ItemMatches { get => _itemMatches; private set => Set(ref _itemMatches, value); }

    private string _itemMatchSummary = "";
    public string ItemMatchSummary { get => _itemMatchSummary; private set => Set(ref _itemMatchSummary, value); }

    private IReadOnlyList<string> _vehicleMatches = Array.Empty<string>();
    public IReadOnlyList<string> VehicleMatches { get => _vehicleMatches; private set => Set(ref _vehicleMatches, value); }

    private string _vehicleMatchSummary = "";
    public string VehicleMatchSummary { get => _vehicleMatchSummary; private set => Set(ref _vehicleMatchSummary, value); }

    // ---- category filter (keyword-derived; see Core ItemCategory). "All" plus the categories that have codes.

    private IReadOnlyList<CategoryOption> _itemCategoryOptions = new[] { new CategoryOption(ItemCategory.All, 0) };
    public IReadOnlyList<CategoryOption> ItemCategoryOptions { get => _itemCategoryOptions; private set => Set(ref _itemCategoryOptions, value); }

    private CategoryOption? _selectedItemCategory;
    public CategoryOption? SelectedItemCategory
    {
        get => _selectedItemCategory;
        set { if (Set(ref _selectedItemCategory, value)) RefreshItemMatches(); }
    }

    private IReadOnlyList<CategoryOption> _vehicleCategoryOptions = new[] { new CategoryOption(ItemCategory.All, 0) };
    public IReadOnlyList<CategoryOption> VehicleCategoryOptions { get => _vehicleCategoryOptions; private set => Set(ref _vehicleCategoryOptions, value); }

    private CategoryOption? _selectedVehicleCategory;
    public CategoryOption? SelectedVehicleCategory
    {
        get => _selectedVehicleCategory;
        set { if (Set(ref _selectedVehicleCategory, value)) RefreshVehicleMatches(); }
    }

    // Rebuilds the "All (n)" + per-category options, keeping the current selection by name when it still exists.
    private static IReadOnlyList<CategoryOption> BuildCategoryOptions(IReadOnlyList<string> codes, bool vehicles, ref CategoryOption? selected)
    {
        var options = new List<CategoryOption> { new(ItemCategory.All, codes.Count) };
        options.AddRange(ItemCategory.Counts(codes, vehicles).Select(c => new CategoryOption(c.Name, c.Count)));
        var keepName = selected?.Name ?? ItemCategory.All;
        selected = options.FirstOrDefault(o => o.Name == keepName) ?? options[0];
        return options;
    }

    /// <summary>Rebuild the known-code list from the item table plus codes already in kits and favorites.</summary>
    private void RebuildItemCatalog()
    {
        var kitCodes = Kits.SelectMany(k => k.Items).Select(i => i.Code).OfType<string>();
        var kitVehicles = kitCodes.Where(c => c.StartsWith("#spawnvehicle ", StringComparison.OrdinalIgnoreCase))
                                  .Select(c => c["#spawnvehicle ".Length..].Trim());
        var table = LoadSahItemMap().Values;
        _gameItems = GameItemList.Load(AppPaths.ActiveGameItems);
        _itemCatalog = new ItemCatalog(table, kitCodes.Concat(FavoriteItems), kitVehicles.Concat(FavoriteVehicles), _gameItems?.Items);
        var catalog = _itemCatalog;
        UnlistedCodeVisibilityConverter.IsUnlisted = catalog.IsUnlisted;
        Raise(nameof(GameItemsTip));
        ItemCategoryOptions = BuildCategoryOptions(_itemCatalog.Items, vehicles: false, ref _selectedItemCategory);
        Raise(nameof(SelectedItemCategory));
        VehicleCategoryOptions = BuildCategoryOptions(_itemCatalog.Vehicles, vehicles: true, ref _selectedVehicleCategory);
        Raise(nameof(SelectedVehicleCategory));
        RefreshItemMatches();
        RefreshVehicleMatches();
    }

    private void RefreshItemMatches()
    {
        var pool = ItemCategory.InCategory(_itemCatalog.Items, _selectedItemCategory?.Name);
        ItemMatches = ItemCatalog.Filter(pool, ItemCode, MaxShownMatches, _itemCatalog.Unlisted);
        ItemMatchSummary = MatchSummary(ItemCatalog.Count(pool, ItemCode), pool.Count);
    }

    // ---- the game's own item list (#ListItems, read from SCUM's client log)

    private GameItemList.FileModel? _gameItems;
    private ICommand? _refreshGameItems;

    public string GameItemsTip => _gameItems is { } g
        ? $"Item list: {g.Items.Count} codes from #ListItems, read {g.ReadAt:MMM d yyyy HH:mm}. Codes the game doesn't list show \"not in #ListItems\". "
          + "Refresh sends #ListItems and reads the result from SCUM's log (with Dry run on, it reads the last list already in the log)."
        : "No #ListItems list yet. Refresh sends #ListItems and reads the result from SCUM's log.";

    public ICommand RefreshGameItemsCommand => _refreshGameItems ??= new AsyncCommand(RefreshGameItemsAsync);

    /// <summary>How long to wait for the game to finish printing #ListItems into its log.</summary>
    private const int GameListTimeoutMs = 30_000;

    private async Task RefreshGameItemsAsync()
    {
        var log = AppPaths.ScumClientLog;
        if (!File.Exists(log)) { Status = $"SCUM's log isn't at {log}. Start SCUM once, then try again."; return; }

        List<string>? items;
        if (Settings.DryRun)
        {
            items = GameItemList.LastFullList(await ReadSharedAsync(log, 0));
            if (items == null) { Status = "Dry run: no full #ListItems output in SCUM's log yet. Turn off Dry run to send it."; return; }
        }
        else
        {
            var before = new FileInfo(log).Length;
            if (!await ExecuteAsync(new[] { "#ListItems" })) return;
            items = null;
            for (var waited = 0; waited < GameListTimeoutMs && items == null; waited += 1000)
            {
                await Task.Delay(1000);
                // A restarted game starts a new log, so read it all when it shrank.
                var length = new FileInfo(log).Length;
                items = GameItemList.LastFullList(await ReadSharedAsync(log, length < before ? 0 : before));
            }
            if (items == null) { Status = "#ListItems sent, but no item list reached SCUM's log within 30 s."; return; }
        }

        var old = _gameItems?.Items.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = items.Count(i => !old.Contains(i));
        var removed = old.Count(o => !items.Contains(o, StringComparer.OrdinalIgnoreCase));
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.UserGameItems, GameItemList.Serialize(items, DateTime.Now));
        RebuildItemCatalog();
        Status = $"Item list updated from the game: {items.Count} codes ({added} new, {removed} gone since the last list).";
    }

    /// <summary>Reads the log from <paramref name="offset"/> while the game still has it open for writing.</summary>
    private static async Task<string> ReadSharedAsync(string path, long offset)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(Math.Min(offset, fs.Length), SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        return await reader.ReadToEndAsync();
    }

    private void RefreshVehicleMatches()
    {
        var pool = ItemCategory.InCategory(_itemCatalog.Vehicles, _selectedVehicleCategory?.Name, vehicles: true);
        VehicleMatches = ItemCatalog.Filter(pool, VehicleCode, MaxShownMatches);
        VehicleMatchSummary = MatchSummary(ItemCatalog.Count(pool, VehicleCode), pool.Count);
    }

    private static string MatchSummary(int matched, int total) =>
        matched == total ? $"{total} known"
        : matched > MaxShownMatches ? $"{matched} of {total} match (first {MaxShownMatches} shown)"
        : $"{matched} of {total} match";
}
