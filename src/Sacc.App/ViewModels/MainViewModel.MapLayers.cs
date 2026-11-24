using System.Collections.ObjectModel;
using System.Windows.Input;
using Sacc.App.Infrastructure;
using Sacc.Core;

namespace Sacc.App.ViewModels;

/// <summary>The Map tab's base-map layer stack: tile types (Regular, Tactical, …) plus the temperature
/// overlay, each toggled, reordered and faded independently and stacked over one another.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The stack, top-to-bottom (index 0 draws on top). Bound by the Map tab's layer list.</summary>
    public ObservableCollection<MapLayerVM> MapLayers { get; } = new();

    public ICommand MoveMapLayerUpCommand { get; private set; } = null!;
    public ICommand MoveMapLayerDownCommand { get; private set; } = null!;

    private void InitMapLayers()
    {
        MoveMapLayerUpCommand = new RelayCommand(
            p => MoveMapLayer(p, -1), p => p is MapLayerVM vm && MapLayers.IndexOf(vm) > 0);
        MoveMapLayerDownCommand = new RelayCommand(
            p => MoveMapLayer(p, +1), p => p is MapLayerVM vm && MapLayers.IndexOf(vm) >= 0 && MapLayers.IndexOf(vm) < MapLayers.Count - 1);
    }

    /// <summary>Reconcile the saved stack with the types the loaded map offers, then rebuild the bound list.
    /// Safe to call whenever the map (re)loads or its type list changes.</summary>
    public void RebuildMapLayers()
    {
        var reconciled = Sacc.Core.MapLayers.Reconcile(
            Settings.MapLayers, MapTypeNames, Settings.MapType, Settings.MapShowTemperature);
        Settings.MapLayers = reconciled;

        MapLayers.Clear();
        foreach (var state in reconciled)
        {
            var isTemp = state.Id == Sacc.Core.MapLayers.TemperatureId;
            var name = isTemp ? "Temperature (est.)" : state.Id;
            MapLayers.Add(new MapLayerVM(state, name, isTemp, OnMapLayerChanged));
        }
        Raise(nameof(MapLayers));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>A layer's visibility, opacity or order changed: mirror the derived single-layer settings the
    /// minimap and mission preview still read, persist, and redraw.</summary>
    private void OnMapLayerChanged()
    {
        // Keep MapType pointing at the top visible tile layer, so the minimap/mission fallbacks stay sensible.
        var topBase = MapLayers.FirstOrDefault(l => l is { IsTemperature: false, Visible: true });
        if (topBase != null && Settings.MapType != topBase.Id) { Settings.MapType = topBase.Id; Raise(nameof(MapType)); }

        var temp = MapLayers.FirstOrDefault(l => l.IsTemperature);
        if (temp != null && Settings.MapShowTemperature != temp.Visible)
        {
            Settings.MapShowTemperature = temp.Visible;
            Raise(nameof(MapShowTemperature));
        }

        Save();
        MapChanged?.Invoke();
    }

    private void MoveMapLayer(object? param, int dir)
    {
        if (param is not MapLayerVM vm) return;
        var i = MapLayers.IndexOf(vm);
        var j = i + dir;
        if (i < 0 || j < 0 || j >= MapLayers.Count) return;

        MapLayers.Move(i, j);
        var s = Settings.MapLayers;                 // same order as the bound list; keep them in lockstep
        (s[i], s[j]) = (s[j], s[i]);
        OnMapLayerChanged();
        CommandManager.InvalidateRequerySuggested();
    }
}

/// <summary>Binds one <see cref="MapLayerState"/> for the Map tab's layer list; edits write through to the
/// persisted state and call back to redraw/save.</summary>
public sealed class MapLayerVM : ObservableObject
{
    private readonly MapLayerState _state;
    private readonly Action _changed;

    public MapLayerVM(MapLayerState state, string displayName, bool isTemperature, Action changed)
    {
        _state = state;
        DisplayName = displayName;
        IsTemperature = isTemperature;
        _changed = changed;
    }

    public string Id => _state.Id;
    public string DisplayName { get; }
    public bool IsTemperature { get; }

    public bool Visible
    {
        get => _state.Visible;
        set { if (_state.Visible == value) return; _state.Visible = value; Raise(); _changed(); }
    }

    public double Opacity
    {
        get => _state.Opacity;
        set
        {
            var v = Sacc.Core.MapLayers.ClampOpacity(value);
            if (_state.Opacity == v) return;
            _state.Opacity = v;
            Raise();
            Raise(nameof(OpacityPercent));
            _changed();
        }
    }

    public int OpacityPercent => (int)Math.Round(Opacity * 100);
}
