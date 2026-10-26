using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Sacc.App.Infrastructure;
using Sacc.Core;

namespace Sacc.App.ViewModels;

public sealed partial class MainViewModel
{
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

    /// <summary>Rebuild the known-code list from the item table plus codes already in kits and favorites.</summary>
    private void RebuildItemCatalog()
    {
        var kitCodes = Kits.SelectMany(k => k.Items).Select(i => i.Code).OfType<string>();
        var kitVehicles = kitCodes.Where(c => c.StartsWith("#spawnvehicle ", StringComparison.OrdinalIgnoreCase))
                                  .Select(c => c["#spawnvehicle ".Length..].Trim());
        var table = LoadSahItemMap().Values;
        _itemCatalog = new ItemCatalog(table, kitCodes.Concat(FavoriteItems), kitVehicles.Concat(FavoriteVehicles));
        RefreshItemMatches();
        RefreshVehicleMatches();
    }

    private void RefreshItemMatches()
    {
        ItemMatches = ItemCatalog.Filter(_itemCatalog.Items, ItemCode, MaxShownMatches);
        ItemMatchSummary = MatchSummary(ItemCatalog.Count(_itemCatalog.Items, ItemCode), _itemCatalog.Items.Count);
    }

    private void RefreshVehicleMatches()
    {
        VehicleMatches = ItemCatalog.Filter(_itemCatalog.Vehicles, VehicleCode, MaxShownMatches);
        VehicleMatchSummary = MatchSummary(ItemCatalog.Count(_itemCatalog.Vehicles, VehicleCode), _itemCatalog.Vehicles.Count);
    }

    private static string MatchSummary(int matched, int total) =>
        matched == total ? $"{total} known"
        : matched > MaxShownMatches ? $"{matched} of {total} match (first {MaxShownMatches} shown)"
        : $"{matched} of {total} match";
}
