using System.Collections;
using System.Globalization;
using System.Windows.Input;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

/// <summary>One row of the GM panel: what to spawn and how many; its command spawns it at the right-clicked spot.</summary>
public sealed class GmActionViewModel : ObservableObject
{
    private readonly GmChoice _choice;
    private readonly Func<IReadOnlyList<string>> _types;

    public GmActionViewModel(GmSpawn.Action action, GmChoice choice, Func<IReadOnlyList<string>> types, ICommand spawn)
    {
        Action = action;
        _choice = choice;
        _types = types;
        SpawnCommand = spawn;
    }

    public GmSpawn.Action Action { get; }
    public string Label => Action.Label;
    public IReadOnlyList<string> Types => _types();
    /// <summary>Re-read the type list (kits change while SACC runs).</summary>
    public void RefreshTypes() => Raise(nameof(Types));
    public bool HasType => Action.Key == "kit" || (Action.TypeParam != null && Action.FixedType == null);
    public ICommand SpawnCommand { get; }

    public string Type
    {
        get => _choice.Type;
        set { if (_choice.Type == (value ?? "")) return; _choice.Type = value ?? ""; Raise(); Raise(nameof(MenuHeader)); }
    }

    public string Count
    {
        get => _choice.Count.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return;
            _choice.Count = Math.Clamp(n, 1, MainViewModel.GmMaxCount);
            Raise(); Raise(nameof(MenuHeader));
        }
    }

    public string MenuHeader => HasType
        ? $"{Label}: {(_choice.Type.Length > 0 ? _choice.Type : "(pick a type)")} ×{_choice.Count}"
        : $"{Label} ×{_choice.Count}";
}

/// <summary>The Map tab's GM panel: right-click a spot to spawn animals, NPCs, zombies, beepers, razors, cargo drops or a kit there.</summary>
public sealed partial class MainViewModel
{
    public const int GmMaxCount = 50;
    /// <summary>How far to look for a scum-map marker whose height can stand in for the ground's.</summary>
    private const double GmHeightSearchWorldUnits = 30_000; // 300 m

    private static readonly Dictionary<string, (string Type, int Count)> GmDefaults = new()
    {
        ["animals"] = ("BP_Wolf3", 3),
        ["npcs"] = ("BP_Drifter_Lvl_3", 3),
        ["zombies"] = ("BP_Zombie_Military_Armored", 5),
        ["beepers"] = ("", 1),
        ["razors"] = ("", 1),
        ["cargo"] = ("", 1),
        ["vehicle"] = ("BPC_RIS", 1),
        ["kit"] = ("", 1),
    };

    private List<GmActionViewModel>? _gmActions;
    private string _gmHeightText = "";
    private Vec3? _gmSpot;

    public IReadOnlyList<GmActionViewModel> GmActions => _gmActions ??= BuildGmActions();

    /// <summary>Shown at the top of the right-click menu: where the spawn will happen and how high.</summary>
    public string GmHeightText { get => _gmHeightText; private set => Set(ref _gmHeightText, value); }

    private List<GmActionViewModel> BuildGmActions()
    {
        // Spawn codes come from the same SAH id table the kit importer uses.
        var codes = LoadSahItemMap().Values.ToList();
        List<string> After(string prefix) => codes
            .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(c => c[prefix.Length..].Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

        var spawn = new AsyncCommand(o => o is GmActionViewModel a ? GmSpawnAsync(a) : Task.CompletedTask, _ => _gmSpot != null);
        var actions = GmSpawn.Actions.Select(a =>
        {
            if (!Settings.Gm.TryGetValue(a.Key, out var choice))
            {
                var d = GmDefaults.GetValueOrDefault(a.Key, ("", 1));
                Settings.Gm[a.Key] = choice = new GmChoice { Type = d.Item1, Count = d.Item2 };
            }
            IReadOnlyList<string> fixedTypes = a.Key switch
            {
                "animals" => After("#spawnanimal "),
                "npcs" => After("#SpawnArmedNPC "),
                "zombies" => After("#spawnzombie "),
                "vehicle" => After("#spawnvehicle "),
                _ => Array.Empty<string>(),
            };
            Func<IReadOnlyList<string>> types = a.Key == "kit"
                ? () => Kits.Select(k => k.Label).Order(StringComparer.OrdinalIgnoreCase).ToList()
                : () => fixedTypes;
            return new GmActionViewModel(a, choice, types, spawn);
        }).ToList();
        Kits.CollectionChanged += (_, _) => actions.First(a => a.Action.Key == "kit").RefreshTypes();
        return actions;
    }

    /// <summary>Called whenever a map spot is picked: work out the spawn height for it.</summary>
    private void UpdateGmSpot()
    {
        if (MapPoint is not { } p) { _gmSpot = null; GmHeightText = ""; return; }
        string source;
        double z;
        if (Settings.GmUseSetHeight) { z = Settings.GmSetHeight; source = "set height"; }
        else if (Settings.GmHeightFromMarkers && MapPointMarker?.Height is { } mh) { z = mh; source = $"from {MapPointMarker.Title}"; }
        else if (NearbyGround(p) is { } g) { z = g.Z; source = g.Source; }
        else { z = Settings.MapTeleportZ; source = "nothing within 300 m: the Map Z setting"; }
        if (Settings.GmHeightOffset != 0)
        {
            z += Settings.GmHeightOffset;
            source += FormattableString.Invariant($", {Settings.GmHeightOffset:+0;-0} offset");
        }
        _gmSpot = p with { Z = z };
        GmHeightText = FormattableString.Invariant($"Spawn at {Grid.KeypadName(p.X, p.Y)}, Z {z:0} ({source})");
    }

    public bool GmUseSetHeight { get => Settings.GmUseSetHeight; set { Settings.GmUseSetHeight = value; Raise(); UpdateGmSpot(); } }
    public string GmSetHeight { get => Num(Settings.GmSetHeight); set { if (TryNum(value, out var v)) { Settings.GmSetHeight = v; Raise(); UpdateGmSpot(); } } }
    public string GmHeightOffset { get => Num(Settings.GmHeightOffset); set { if (TryNum(value, out var v)) { Settings.GmHeightOffset = v; Raise(); UpdateGmSpot(); } } }
    public bool GmHeightFromLocations { get => Settings.GmHeightFromLocations; set { Settings.GmHeightFromLocations = value; Raise(); UpdateGmSpot(); } }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool TryNum(string? s, out double v) =>
        double.TryParse((s ?? "").Trim().Length == 0 ? "0" : s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    public bool GmHeightFromMarkers { get => Settings.GmHeightFromMarkers; set { Settings.GmHeightFromMarkers = value; Raise(); UpdateGmSpot(); } }
    public bool GmHeightFromMe { get => Settings.GmHeightFromMe; set { Settings.GmHeightFromMe = value; Raise(); UpdateGmSpot(); } }
    public bool GmHeightFromPlayers { get => Settings.GmHeightFromPlayers; set { Settings.GmHeightFromPlayers = value; Raise(); UpdateGmSpot(); } }
    public bool GmHeightFromVehicles { get => Settings.GmHeightFromVehicles; set { Settings.GmHeightFromVehicles = value; Raise(); UpdateGmSpot(); } }

    // ---- vehicles (#ListSpawnedVehicles), only used for heights

    private ICommand? _refreshVehicles, _pasteVehicles;

    public string GmVehiclesLabel => _profile.VehiclesListedAt is { } at
        ? $"Nearest vehicle height ({_profile.Vehicles.Count}, listed {at:MMM d HH:mm})"
        : "Nearest vehicle height (not listed yet)";

    public ICommand RefreshVehiclesCommand => _refreshVehicles ??= new AsyncCommand(async () =>
    {
        var def = _catalog.Find("list-spawned-vehicles-copy");
        if (def == null) { Status = "The command catalog has no 'list-spawned-vehicles-copy' entry."; return; }
        if (await CaptureAsync(def.Template) is { } output) ReplaceVehicles(output);
    });

    public ICommand PasteVehiclesCommand => _pasteVehicles ??= new RelayCommand(() =>
    {
        var text = Sacc.App.Game.GameLink.TryGetClipboardText();
        if (string.IsNullOrWhiteSpace(text)) { Status = "Clipboard is empty. Copy the #ListSpawnedVehicles output first."; return; }
        ReplaceVehicles(text);
    });

    private void ReplaceVehicles(string text)
    {
        var parsed = Parsing.ParseVehicleList(text);
        if (parsed.Count == 0) { Status = "No vehicle locations (X= Y= Z=) found in that text."; return; }
        _profile.Vehicles = parsed;
        _profile.VehiclesListedAt = DateTime.Now;
        Raise(nameof(GmVehiclesLabel));
        UpdateGmSpot();
        Status = $"Read {parsed.Count} vehicle position(s).";
        Save();
    }

    /// <summary>
    /// Ground height near a spot, from whichever enabled source is closest within 300 m: the
    /// nearest scum-map marker, your own last known position (Settings → your SteamID64), or
    /// the nearest player's last known position. Old positions are fine: the ground hasn't moved.
    /// </summary>
    private (double Z, string Source)? NearbyGround(Vec3 p)
    {
        (double Z, string Source)? best = null;
        var bestDist = GmHeightSearchWorldUnits;

        if (Settings.GmHeightFromMarkers && _map != null)
        {
            var projection = new MapProjection(_map.Template.Settings);
            var (mx, my) = projection.ToMap(p.X, p.Y);
            var (ex, _) = projection.ToMap(p.X + GmHeightSearchWorldUnits, p.Y);
            var unitsPerMap = GmHeightSearchWorldUnits / Math.Abs(ex - mx);
            if (GmSpawn.NearbyHeight(_map.Markers, mx, my, Math.Abs(ex - mx), out var d) is { } h)
            {
                best = (h, "from the nearest map marker");
                bestDist = d * unitsPerMap;
            }
        }

        var myId = Settings.MySteamId.Trim();
        foreach (var pl in Players)
        {
            if (pl.Location is not { } loc) continue;
            var mine = myId.Length > 0 && pl.SteamId == myId;
            if (mine ? !Settings.GmHeightFromMe : !Settings.GmHeightFromPlayers) continue;
            var d = Math.Sqrt((loc.X - p.X) * (loc.X - p.X) + (loc.Y - p.Y) * (loc.Y - p.Y));
            if (d > bestDist) continue;
            bestDist = d;
            var who = mine ? "your position" : $"{(string.IsNullOrWhiteSpace(pl.Nickname) ? pl.SteamId : pl.Nickname)}'s position";
            best = (loc.Z, FormattableString.Invariant($"from {who} {d / 100:0} m away, seen {pl.LastSeen:MMM d HH:mm}"));
        }

        if (Settings.GmHeightFromLocations)
            foreach (var b in Bookmarks)
            {
                var d = Math.Sqrt((b.X - p.X) * (b.X - p.X) + (b.Y - p.Y) * (b.Y - p.Y));
                if (d > bestDist) continue;
                bestDist = d;
                best = (b.Z, FormattableString.Invariant($"from saved location '{b.Name}' {d / 100:0} m away"));
            }

        if (Settings.GmHeightFromVehicles)
            foreach (var v in _profile.Vehicles)
            {
                var loc = v.Location;
                var d = Math.Sqrt((loc.X - p.X) * (loc.X - p.X) + (loc.Y - p.Y) * (loc.Y - p.Y));
                if (d > bestDist) continue;
                bestDist = d;
                best = (loc.Z, FormattableString.Invariant($"from vehicle {(v.Code.Length > 0 ? v.Code : v.Id)} {d / 100:0} m away"));
            }
        return best;
    }

    private async Task GmSpawnAsync(GmActionViewModel a)
    {
        if (_gmSpot is not { } at) return;
        var count = int.Parse(a.Count, CultureInfo.InvariantCulture);
        List<string> lines;
        var note = "";

        if (a.Action.Key == "kit")
        {
            var kit = Kits.FirstOrDefault(k => k.Label == a.Type) ?? Kits.FirstOrDefault(k => k.Name == a.Type);
            if (kit == null) { Status = "Pick a kit in the GM panel first."; return; }
            var kitLines = KitLines(kit, out var skipped);
            if (kitLines == null) return;
            var placed = GmSpawn.KitAt(kitLines, at, out var notPlaced);
            lines = Enumerable.Repeat(placed, count).SelectMany(l => l).ToList();
            if (notPlaced > 0) note += $" {notPlaced} kit line(s) that aren't #SpawnItem will happen at you, not the spot.";
            if (skipped > 0) note += $" {skipped} unresolved item(s) left out.";
        }
        else
        {
            if (a.HasType && string.IsNullOrWhiteSpace(a.Type)) { Status = $"Pick a type for {a.Label} in the GM panel first."; return; }
            var def = _catalog.Find(a.Action.CatalogId);
            if (def == null) { Status = $"The command catalog has no '{a.Action.CatalogId}' entry."; return; }
            var r = GmSpawn.Render(def, a.Action, a.Type, count, at);
            if (!r.Ok) { Status = $"{a.Label}: {string.Join("  ", r.Errors)}"; return; }
            lines = r.Text.Split('\n').ToList();
        }

        if (lines.Count == 0) { Status = "Nothing to spawn."; return; }
        await ExecuteAsync(lines);
        Status += note;
    }
}
