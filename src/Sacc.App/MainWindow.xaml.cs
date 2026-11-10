using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Sacc.App.ViewModels;

namespace Sacc.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(
            confirm: msg => MessageBox.Show(this, msg, "SACC", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes,
            ownWindow: () => new WindowInteropHelper(this).Handle);
        DataContext = _vm;
        WireMap();
    }

    // ---- Map tab: the MapView is a plain control fed from the view model.

    private void WireMap()
    {
        MapCanvas.Data = _vm.MapDataService;
        MapCanvas.Players = _vm.Players;
        MapCanvas.Bookmarks = _vm.Bookmarks;
        MapCanvas.Flags = _vm.Flags;
        MapCanvas.Hover += _vm.MapHovered;
        MapCanvas.Clicked += _vm.MapPointPicked;
        MapCanvas.ContextRequested += _vm.MapPointPicked;
        _vm.MapChanged += SyncMap;
        _vm.MapGoRequested += (world, zoom) => MapCanvas.CenterOn(world.X, world.Y, zoom);

        MissionCanvas.Data = _vm.MapDataService;
        MissionCanvas.Players = _vm.Players;
        MissionCanvas.Clicked += (world, _) => _vm.MissionMapClicked(world, MissionCanvas.PixelDistance);
        _vm.MissionGoRequested += (world, radius) => MissionCanvas.Frame(world.X, world.Y, radius);

        var tabs = (TabControl)MapTab.Parent;
        tabs.SelectionChanged += async (_, e) =>
        {
            // SelectionChanged bubbles up from lists inside tabs too; only react to the tab strip itself.
            if (e.OriginalSource == tabs && (tabs.SelectedItem == MapTab || tabs.SelectedItem == MissionTab))
            {
                await _vm.EnsureMapLoadedAsync();
                // After layout: framing needs the map's size.
                if (tabs.SelectedItem == MissionTab) _ = Dispatcher.BeginInvoke(_vm.FrameSelectedPhase, System.Windows.Threading.DispatcherPriority.Background);
            }
        };
    }

    private void SyncMap()
    {
        if (_vm.MapTemplate is { } t && _vm.ActiveTileTemplate is { } tiles) MapCanvas.SetMap(t, tiles);
        MapCanvas.Categories = _vm.MapCategoryLookup;
        MapCanvas.Markers = _vm.VisibleMapMarkers;
        MapCanvas.PlayersListedAt = _vm.PlayersListedAt;
        MapCanvas.ShowOnline = _vm.MapShowOnline;
        MapCanvas.ShowPlayers = _vm.MapShowPlayers;
        MapCanvas.ShowFlags = _vm.MapShowFlags;
        MapCanvas.Vehicles = _vm.MapVehicles;
        MapCanvas.ShowVehicles = _vm.MapShowVehicles;
        MapCanvas.LabelVehicleIds = _vm.MapVehicleIdFilter.Length > 0;
        MapCanvas.Tracks = _vm.MapTracks;
        MapCanvas.ShowTracks = _vm.MapShowTracks;
        MapCanvas.ShowBookmarks = _vm.MapShowBookmarks;
        MapCanvas.Features = _vm.MapFeatures;
        MapCanvas.ShowThreatZones = _vm.MapShowThreatZones;
        MapCanvas.ShowHuntingAreas = _vm.MapShowHuntingAreas;
        MapCanvas.ShowPatrolPaths = _vm.MapShowPatrolPaths;
        MapCanvas.ShowLocations = _vm.MapShowLocations;
        MapCanvas.Terrain = _vm.MapTerrainGrid;
        MapCanvas.ShowTemperature = _vm.MapShowTemperature;
        MapCanvas.GridStyle = _vm.MapGrid;
        if (_vm.MapTemplate is { } mt && _vm.ActiveTileTemplate is { } mtiles) MissionCanvas.SetMap(mt, mtiles);
        MissionCanvas.Categories = _vm.MapCategoryLookup;
        MissionCanvas.PlayersListedAt = _vm.PlayersListedAt;
        MissionCanvas.ShowOnline = _vm.MapShowOnline;
        MissionCanvas.Mission = _vm.SelectedMission;
        MissionCanvas.SelectedPhase = _vm.SelectedPhase;
        MissionCanvas.SelectedSpawn = _vm.SelectedSpawn;
        if (Sacc.App.Map.MapDataService.WebpUnsupported) _vm.NoteWebpMissing();
    }

    private void ViewOnScumMap_Click(object sender, RoutedEventArgs e)
    {
        // scum-map.com's own URL format: /en/scum/island/X,Y,zoom (centre of view, world coordinates).
        var (x, y) = _vm.MapPoint is { } p ? (p.X, p.Y) : MapCanvas.CenterWorld;
        var zoom = Math.Clamp((int)Math.Round(MapCanvas.Zoom), 0, 8);
        _vm.OpenScumMapCommand.Execute(FormattableString.Invariant($"{x:0.####},{y:0.####},{Math.Max(zoom, 4)}"));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.Save();
        base.OnClosing(e);
    }

    private void MissionTab_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) _vm.ToolSelect = true;
    }

    private void FreeformBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_vm.SendFreeformCommand.CanExecute(null)) _vm.SendFreeformCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _vm.RecallHistory(+1);
                FreeformBox.CaretIndex = FreeformBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Down:
                _vm.RecallHistory(-1);
                FreeformBox.CaretIndex = FreeformBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    // ---- Spawner type-ahead: the code box filters the list; Down moves into it, Enter picks or spawns.

    private void ItemCodeBox_PreviewKeyDown(object sender, KeyEventArgs e) =>
        CodeBoxKey(e, ItemMatchesList, () => _vm.SpawnItemCommand.Execute(null));

    private void VehicleCodeBox_PreviewKeyDown(object sender, KeyEventArgs e) =>
        CodeBoxKey(e, VehicleMatchesList, () => _vm.SpawnVehicleCommand.Execute(null));

    private static void CodeBoxKey(KeyEventArgs e, ListBox list, Action spawn)
    {
        if (e.Key == Key.Down && list.Items.Count > 0)
        {
            list.SelectedIndex = 0;
            list.UpdateLayout();
            (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            spawn();
            e.Handled = true;
        }
    }

    private void ItemMatches_Click(object sender, MouseButtonEventArgs e) => PickFrom(ItemMatchesList, c => _vm.ItemCode = c, ItemCodeBox, focus: false);
    private void VehicleMatches_Click(object sender, MouseButtonEventArgs e) => PickFrom(VehicleMatchesList, c => _vm.VehicleCode = c, VehicleCodeBox, focus: false);

    private void ItemMatches_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemMatchesList.SelectedItem is string code) _vm.SpawnItemCommand.Execute(code);
    }

    private void VehicleMatches_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VehicleMatchesList.SelectedItem is string code) _vm.SpawnVehicleCommand.Execute(code);
    }

    private void ItemMatches_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PickFrom(ItemMatchesList, c => _vm.ItemCode = c, ItemCodeBox, focus: true); e.Handled = true; }
    }

    private void VehicleMatches_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PickFrom(VehicleMatchesList, c => _vm.VehicleCode = c, VehicleCodeBox, focus: true); e.Handled = true; }
    }

    private static void PickFrom(ListBox list, Action<string> set, TextBox box, bool focus)
    {
        if (list.SelectedItem is not string code) return;
        set(code);
        if (!focus) return;
        box.Focus();
        box.CaretIndex = box.Text.Length;
    }

    private void KitOption_Click(object sender, RoutedEventArgs e) => _vm.Save();

    private void KitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KitList.SelectedItem != null) KitList.ScrollIntoView(KitList.SelectedItem);
    }

    // Kit name edits: refresh the list label and save.
    private void KitDetail_LostFocus(object sender, RoutedEventArgs e) => _vm.KitItemsChanged();

    // ---- Kit add-item row: Down enters the suggestions, Enter picks (in the list) or adds (in the box).

    private void NewItemBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _vm.NewItemPopupOpen && NewItemSuggestions.Items.Count > 0:
                NewItemSuggestions.SelectedIndex = 0;
                NewItemSuggestions.UpdateLayout();
                (NewItemSuggestions.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Escape when _vm.NewItemPopupOpen:
                _vm.NewItemPopupOpen = false;
                e.Handled = true; // don't let Esc also trigger Stop
                break;
            case Key.Enter:
                // With suggestions showing and nothing exact typed yet, Enter takes the top suggestion first.
                if (_vm.NewItemPopupOpen && _vm.NewItemMatches.Count > 0) _vm.PickNewItem(_vm.NewItemMatches[0]);
                else if (_vm.AddKitItemCommand.CanExecute(null)) _vm.AddKitItemCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void NewItemField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_vm.AddKitItemCommand.CanExecute(null)) _vm.AddKitItemCommand.Execute(null);
        NewItemBox.Focus();
        e.Handled = true;
    }

    private void NewItemSuggestions_Click(object sender, MouseButtonEventArgs e) => PickSuggestion();

    private void NewItemSuggestions_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PickSuggestion(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _vm.NewItemPopupOpen = false; NewItemBox.Focus(); e.Handled = true; }
    }

    private void PickSuggestion()
    {
        if (NewItemSuggestions.SelectedItem is not string code) return;
        _vm.PickNewItem(code);
        NewItemBox.Focus();
        NewItemBox.CaretIndex = NewItemBox.Text.Length;
    }

    private void ItemFavorites_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemFavorites.SelectedItem is string code) _vm.SpawnItemCommand.Execute(code);
    }

    private void VehicleFavorites_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VehicleFavorites.SelectedItem is string code) _vm.SpawnVehicleCommand.Execute(code);
    }

    private void ContainerPresets_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: Sacc.Core.Model.ContainerPreset p }) _vm.SpawnContainerPresetCommand.Execute(p);
    }

    private void Bookmarks_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(_vm.Save));

    // Bookmarks are plain objects, so the Sector column only recomputes after an edited X/Y row is committed.
    private void BookmarksGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => BookmarksGrid.Items.Refresh()), System.Windows.Threading.DispatcherPriority.Background);

    // Row add/delete changes the item count shown in the kit list, so refresh it after saving.
    private void KitItems_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(_vm.KitItemsChanged));

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.Delete && e.OriginalSource is DataGridCell or DataGridRow) _vm.KitItemsChanged();
    }
}
