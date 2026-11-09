using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.Map;

/// <summary>
/// A lightweight slippy map in the same space as scum-map.com's Leaflet map: map units
/// (0..320 both ways, y down), and at zoom z one map unit is 2^z pixels. Draws tiles from
/// <see cref="MapDataService"/>, then markers, saved locations and players on top.
/// </summary>
public sealed class MapView : FrameworkElement
{
    public MapDataService? Data { get; set; }

    private MapTemplate? _template;
    private MapProjection? _projection;
    private string? _tileTemplate;
    private double _zoom = 1.5, _cx = 160, _cy = 160;
    private Point? _dragStart;
    private (double X, double Y) _dragCenter;
    private bool _dragged;

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x1B, 0x1F)));
    private static readonly Brush PlaceholderFill = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x30)));
    private static readonly Pen MarkerOutline = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Brush PlayerFill = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0xFF, 0x7A)));
    private static readonly Brush OfflineFill = Frozen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)));
    private static readonly Brush FlagFill = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x3A, 0x3A)));
    private static readonly Pen FlagPole = Frozen(new Pen(Brushes.Black, 2));
    private static readonly Brush VehicleFill = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0xA8, 0xE0)));
    private static readonly Brush BookmarkFill = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x2E)));
    private static readonly Brush LabelBack = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)));
    private static readonly Typeface LabelFace = new("Segoe UI");
    private readonly Dictionary<string, Brush> _brushes = new();

    public IReadOnlyList<MapMarker> Markers { get => _markers; set { _markers = value ?? Array.Empty<MapMarker>(); InvalidateVisual(); } }
    private IReadOnlyList<MapMarker> _markers = Array.Empty<MapMarker>();

    public IReadOnlyDictionary<int, MapCategory> Categories { get; set; } = new Dictionary<int, MapCategory>();

    public IEnumerable<PlayerRecord>? Players { get => _players; set { Rebind(ref _players, value); } }
    private IEnumerable<PlayerRecord>? _players;

    public IEnumerable<Bookmark>? Bookmarks { get => _bookmarks; set { Rebind(ref _bookmarks, value); } }
    private IEnumerable<Bookmark>? _bookmarks;

    public IEnumerable<FlagRecord>? Flags { get => _flags; set { Rebind(ref _flags, value); } }
    private IEnumerable<FlagRecord>? _flags;

    /// <summary>When the last #ListPlayers was read; players seen then count as online.</summary>
    public DateTime? PlayersListedAt { get => _listedAt; set { _listedAt = value; InvalidateVisual(); } }
    private DateTime? _listedAt;

    public bool ShowOnline { get => _showOnline; set { _showOnline = value; InvalidateVisual(); } }
    private bool _showOnline = true;
    /// <summary>Players not in the last list, at their last known position.</summary>
    public bool ShowPlayers { get => _showPlayers; set { _showPlayers = value; InvalidateVisual(); } }
    private bool _showPlayers;
    public bool ShowFlags { get => _showFlags; set { _showFlags = value; InvalidateVisual(); } }
    private bool _showFlags;
    /// <summary>Vehicles from the last #ListSpawnedVehicles output.</summary>
    public IReadOnlyList<VehicleRecord> Vehicles { get => _vehicles; set { _vehicles = value ?? Array.Empty<VehicleRecord>(); InvalidateVisual(); } }
    private IReadOnlyList<VehicleRecord> _vehicles = Array.Empty<VehicleRecord>();
    public bool ShowVehicles { get => _showVehicles; set { _showVehicles = value; InvalidateVisual(); } }
    public bool LabelVehicleIds { get => _labelVehicleIds; set { _labelVehicleIds = value; InvalidateVisual(); } }
    private bool _labelVehicleIds;
    private bool _showVehicles;
    /// <summary>Player tracks, one list per unbroken run of positions.</summary>
    public IReadOnlyList<IReadOnlyList<TrackPoint>> Tracks { get => _tracks; set { _tracks = value ?? Array.Empty<IReadOnlyList<TrackPoint>>(); InvalidateVisual(); } }
    private IReadOnlyList<IReadOnlyList<TrackPoint>> _tracks = Array.Empty<IReadOnlyList<TrackPoint>>();
    public bool ShowTracks { get => _showTracks; set { _showTracks = value; InvalidateVisual(); } }
    private bool _showTracks;
    public bool ShowBookmarks { get => _showBookmarks; set { _showBookmarks = value; InvalidateVisual(); } }
    private bool _showBookmarks = true;

    /// <summary>The island's map overlays (threat zones, hunting areas); null when none are loaded.</summary>
    public MapFeatures? Features { get => _features; set { _features = value; InvalidateVisual(); } }
    private MapFeatures? _features;
    public bool ShowThreatZones { get => _showThreat; set { _showThreat = value; InvalidateVisual(); } }
    private bool _showThreat;
    public bool ShowHuntingAreas { get => _showHunting; set { _showHunting = value; InvalidateVisual(); } }
    private bool _showHunting;

    public GridStyle GridStyle { get => _gridStyle; set { _gridStyle = value; InvalidateVisual(); } }
    private GridStyle _gridStyle = GridStyle.Sectors;
    private SectorGrid? _grid;

    private static readonly Pen SectorPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), 2));
    private static readonly Pen KeypadPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 1));
    private static readonly Brush GridText = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush GridHalo = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)));
    private static readonly Typeface GridFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    // Map-features overlays (threat zones, hunting areas): a translucent fill under a solid outline, coloured by tier/biome.
    private static readonly (Pen Pen, Brush Fill) ThreatHigh = AreaStyle("#E53935");
    private static readonly (Pen Pen, Brush Fill) ThreatMedium = AreaStyle("#FFB300");
    private static readonly (Pen Pen, Brush Fill) ThreatLow = AreaStyle("#7CB342");
    private static readonly (Pen Pen, Brush Fill) ThreatNone = AreaStyle("#9E9E9E");
    private static readonly (Pen Pen, Brush Fill) HuntingGame = AreaStyle("#8D6E4A");
    private static readonly (Pen Pen, Brush Fill) HuntingDen = AreaStyle("#8E44AD");

    private static (Pen, Brush) AreaStyle(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var pen = Frozen(new Pen(new SolidColorBrush(c), 1.6));
        var fill = Frozen(new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B)));
        return (pen, fill);
    }

    /// <summary>World point and nearest marker under the mouse; raised on move, click and right-click.</summary>
    public event Action<Vec3?, MapMarker?>? Hover;
    public event Action<Vec3, MapMarker?>? Clicked;
    public event Action<Vec3, MapMarker?>? ContextRequested;

    public double Zoom => _zoom;
    public (double X, double Y) CenterWorld => _projection == null ? (0, 0) : (_projection.ToWorld(_cx, _cy).X, _projection.ToWorld(_cx, _cy).Y);

    public MapView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    public void SetMap(MapTemplate template, string tileTemplate)
    {
        var first = _template == null;
        _template = template;
        _projection = new MapProjection(template.Settings);
        _grid = new SectorGrid(_projection);
        _tileTemplate = tileTemplate;
        if (first) { _cx = _projection.MapWidth / 2; _cy = _projection.MapHeight / 2; }
        InvalidateVisual();
    }

    /// <summary>Centre on a world position (e.g. a player or saved location).</summary>
    public void CenterOn(double worldX, double worldY, double? zoom = null)
    {
        if (_projection == null) return;
        (_cx, _cy) = _projection.ToMap(worldX, worldY);
        if (zoom is { } z) _zoom = Math.Clamp(z, 0, 8);
        InvalidateVisual();
    }

    /// <summary>Centre on a world position, zoomed so this many world units around it fit the view.</summary>
    public void Frame(double worldX, double worldY, double worldRadius)
    {
        if (_projection == null) return;
        (_cx, _cy) = _projection.ToMap(worldX, worldY);
        var (edge, _) = _projection.ToMap(worldX + Math.Max(worldRadius, 1000), worldY);
        var units = Math.Abs(edge - _cx);
        var pixels = Math.Min(ActualWidth, ActualHeight) * 0.42;
        if (units > 0 && pixels > 0) _zoom = Math.Clamp(Math.Log2(pixels / units), 0, 8);
        InvalidateVisual();
    }

    private void Rebind<T>(ref IEnumerable<T>? field, IEnumerable<T>? value)
    {
        if (field is INotifyCollectionChanged oldC) oldC.CollectionChanged -= OnOverlayChanged;
        field = value;
        if (field is INotifyCollectionChanged newC) newC.CollectionChanged += OnOverlayChanged;
        InvalidateVisual();
    }

    private void OnOverlayChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    // ---------------------------------------------------------------- coordinates

    private double Scale => Math.Pow(2, _zoom);

    private Point ToScreen(double mx, double my) =>
        new((mx - _cx) * Scale + ActualWidth / 2, (my - _cy) * Scale + ActualHeight / 2);

    private (double X, double Y) ToMapUnits(Point p) =>
        (_cx + (p.X - ActualWidth / 2) / Scale, _cy + (p.Y - ActualHeight / 2) / Scale);

    // ---------------------------------------------------------------- drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (_template == null || _projection == null || _tileTemplate == null || Data == null) return;

        var s = _template.Settings;
        var scale = Scale;
        // Use the native tile level at or just above the view zoom so tiles are never upscaled much.
        var nz = Math.Clamp((int)Math.Ceiling(_zoom), s.MinNativeZoom, s.MaxNativeZoom);
        var tileUnits = s.TileSize / Math.Pow(2, nz);
        var tilesX = (int)Math.Ceiling(_projection.MapWidth / tileUnits);
        var tilesY = (int)Math.Ceiling(_projection.MapHeight / tileUnits);

        var (left, top) = ToMapUnits(new Point(0, 0));
        var (right, bottom) = ToMapUnits(new Point(ActualWidth, ActualHeight));
        var x0 = Math.Max(0, (int)Math.Floor(left / tileUnits)); var x1 = Math.Min(tilesX - 1, (int)Math.Floor(right / tileUnits));
        var y0 = Math.Max(0, (int)Math.Floor(top / tileUnits)); var y1 = Math.Min(tilesY - 1, (int)Math.Floor(bottom / tileUnits));

        for (var ty = y0; ty <= y1; ty++)
        for (var tx = x0; tx <= x1; tx++)
        {
            var origin = ToScreen(tx * tileUnits, ty * tileUnits);
            var rect = new Rect(origin.X, origin.Y, tileUnits * scale + 0.5, tileUnits * scale + 0.5);
            var task = Data.GetTileAsync(_tileTemplate, nz, tx, ty);
            if (task.IsCompletedSuccessfully && task.Result != null) dc.DrawImage(task.Result, rect);
            else
            {
                dc.DrawRectangle(PlaceholderFill, null, rect);
                if (!task.IsCompleted) task.ContinueWith(_ => InvalidateVisual(), TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        DrawGrid(dc);
        DrawFeatures(dc);

        // Markers: small enough not to bury the map when zoomed out.
        var r = Math.Clamp(2 + _zoom * 1.2, 3, 9);
        foreach (var m in _markers)
        {
            if (m.MapX < left - 1 || m.MapX > right + 1 || m.MapY < top - 1 || m.MapY > bottom + 1) continue;
            var p = ToScreen(m.MapX, m.MapY);
            Categories.TryGetValue(m.CategoryId, out var cat);
            dc.DrawEllipse(BrushFor(cat?.Background ?? "#888888"), MarkerOutline, p, r, r);
            if (_zoom >= 5.5) Label(dc, m.Title, p, r);
        }

        if (_showBookmarks && _bookmarks != null)
            foreach (var b in _bookmarks)
            {
                var (mx, my) = _projection.ToMap(b.X, b.Y);
                var p = ToScreen(mx, my);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(p.X, p.Y - 8), true, true);
                    ctx.PolyLineTo(new[] { new Point(p.X + 6, p.Y), new Point(p.X, p.Y + 8), new Point(p.X - 6, p.Y) }, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(BookmarkFill, MarkerOutline, g);
                Label(dc, b.Name, p, 8);
            }

        if (_showFlags && _flags != null)
            foreach (var f in _flags)
            {
                var (mx, my) = _projection.ToMap(f.Location.X, f.Location.Y);
                var p = ToScreen(mx, my);
                // A pole with a pennant; the pole's foot is the flag's position.
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(p.X, p.Y - 14), true, true);
                    ctx.PolyLineTo(new[] { new Point(p.X + 10, p.Y - 10), new Point(p.X, p.Y - 6) }, true, true);
                }
                g.Freeze();
                dc.DrawLine(FlagPole, new Point(p.X, p.Y), new Point(p.X, p.Y - 14));
                dc.DrawGeometry(FlagFill, MarkerOutline, g);
                if (_zoom >= 3) Label(dc, f.Label, p, 6);
            }

        if (_showVehicles)
            foreach (var v in _vehicles)
            {
                var (mx, my) = _projection.ToMap(v.Location.X, v.Location.Y);
                var p = ToScreen(mx, my);
                dc.DrawRoundedRectangle(VehicleFill, MarkerOutline, new Rect(p.X - 6, p.Y - 4, 12, 8), 2, 2);
                var name = v.Code.StartsWith("BPC_", StringComparison.OrdinalIgnoreCase) ? v.Code[4..] : v.Code;
                // While filtering by car ID, the few cars left are labelled with their ID at any zoom.
                if (_labelVehicleIds)
                {
                    var owner = v.OwnerSteamId.Length == 0 ? null
                        : _players?.FirstOrDefault(pl => pl.SteamId == v.OwnerSteamId) is { } o && o.Nickname.Length > 0 ? o.Nickname : v.OwnerSteamId;
                    Label(dc, (v.Id.Length > 0 ? $"{name} #{v.Id}" : name) + (owner != null ? $" ({owner})" : ""), p, 6);
                }
                else if (_zoom >= 3) Label(dc, name, p, 6);
            }

        if (_showTracks) DrawTracks(dc);
        DrawMission(dc);

        if ((_showPlayers || _showOnline) && _players != null)
            foreach (var pl in _players)
            {
                if (pl.Location is not { } loc) continue;
                var online = _listedAt is { } at && pl.LastSeen >= at;
                if (online ? !_showOnline : !_showPlayers) continue;
                var (mx, my) = _projection.ToMap(loc.X, loc.Y);
                var p = ToScreen(mx, my);
                dc.DrawEllipse(online ? PlayerFill : OfflineFill, MarkerOutline, p, 6, 6);
                Label(dc, string.IsNullOrWhiteSpace(pl.Nickname) ? pl.SteamId : pl.Nickname, p, 6);
            }
    }

    // Saturated enough to read on the map's greens and greys; a player keeps their colour.
    private static readonly Pen[] TrackPens = new[] { "#FF4D4D", "#FFB300", "#00E5FF", "#FF40FF", "#7CFF4D", "#FFFFFF", "#4D7CFF", "#FF8A65" }
        .Select(c => { var pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)), 2.5) { LineJoin = PenLineJoin.Round }; pen.Freeze(); return pen; }).ToArray();

    /// <summary>A line for each run: hollow ring and time where it starts, solid dot and time where it ends.</summary>
    private void DrawTracks(DrawingContext dc)
    {
        if (_projection == null) return;
        foreach (var run in _tracks)
        {
            var hash = 0;
            foreach (var c in run[0].SteamId) hash = unchecked(hash * 31 + c);
            var pen = TrackPens[(hash & int.MaxValue) % TrackPens.Length];

            var points = new List<Point>(run.Count);
            foreach (var t in run)
            {
                var (mx, my) = _projection.ToMap(t.Location.X, t.Location.Y);
                var p = ToScreen(mx, my);
                // Points closer than a couple of pixels add nothing but work.
                if (points.Count == 0 || (p - points[^1]).LengthSquared >= 4) points.Add(p);
            }
            if (points.Count > 1)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(points[0], false, false);
                    ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
            dc.DrawEllipse(Brushes.Black, pen, points[0], 4, 4);
            dc.DrawEllipse(pen.Brush, MarkerOutline, points[^1], 4, 4);
            if (_zoom < 3) continue;
            var who = string.IsNullOrWhiteSpace(run[0].Name) ? run[0].SteamId : run[0].Name;
            Label(dc, $"{who} {TrackTime(run[0].At)}", points[0], 5);
            if (run.Count > 1) Label(dc, TrackTime(run[^1].At), points[^1], 5);
        }
    }

    private static string TrackTime(DateTimeOffset at) =>
        at.LocalDateTime.ToString(at.LocalDateTime.Date == DateTime.Today ? "HH:mm" : "MMM d HH:mm", CultureInfo.CurrentCulture);

    // ---------------------------------------------------------------- the mission overlay

    /// <summary>The mission tab's plan, drawn over the map.</summary>
    public Mission? Mission { get => _mission; set { _mission = value; InvalidateVisual(); } }
    private Mission? _mission;
    public MissionPhase? SelectedPhase { get => _selPhase; set { _selPhase = value; InvalidateVisual(); } }
    private MissionPhase? _selPhase;
    public MissionSpawn? SelectedSpawn { get => _selSpawn; set { _selSpawn = value; InvalidateVisual(); } }
    private MissionSpawn? _selSpawn;

    private static readonly Pen RoutePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), 2) { DashStyle = DashStyles.Dash });
    private static readonly Pen PhaseRing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1E)), 2));
    private static readonly Brush PhaseArea = Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0x8A, 0x1E)));
    private static readonly Brush PhaseBadge = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1E)));
    private static readonly Brush PhaseBadgeDim = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x6A)));
    private static readonly Pen TriggerRing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4D)), 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen HoldRing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)), 1.5) { DashStyle = DashStyles.Dot });
    private static readonly Pen SelectedPin = Frozen(new Pen(Brushes.White, 2.5));
    private static readonly Brush PinPending = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEE)));
    private static readonly Brush PinSpawned = Frozen(new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64)));
    private static readonly Brush PinPartial = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));
    private static readonly Brush PinRefused = Frozen(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)));
    private static readonly Brush PinSent = Frozen(new SolidColorBrush(Color.FromRgb(0x4D, 0xA6, 0xFF)));

    private double WorldToPixels(Vec3 at, double worldUnits)
    {
        var (x0, _) = _projection!.ToMap(at.X, at.Y);
        var (x1, _) = _projection.ToMap(at.X + worldUnits, at.Y);
        return Math.Abs(x1 - x0) * Scale;
    }

    private Point WorldToScreen(Vec3 at)
    {
        var (mx, my) = _projection!.ToMap(at.X, at.Y);
        return ToScreen(mx, my);
    }

    /// <summary>On-screen distance between two world spots, for clicking pins at any zoom.</summary>
    public double PixelDistance(Vec3 a, Vec3 b) => _projection == null ? double.MaxValue : (WorldToScreen(a) - WorldToScreen(b)).Length;

    private void DrawMission(DrawingContext dc)
    {
        if (_mission == null || _projection == null) return;

        // The route first, so everything else sits on it.
        var centres = _mission.Phases.Where(p => p.Center != null).Select(p => WorldToScreen(p.Center!.Value)).ToList();
        for (var i = 1; i < centres.Count; i++) dc.DrawLine(RoutePen, centres[i - 1], centres[i]);

        if (_selPhase?.Center is { } sc)
        {
            var r = WorldToPixels(sc, _selPhase.RadiusM * 100);
            dc.DrawEllipse(PhaseArea, PhaseRing, WorldToScreen(sc), r, r);
        }

        if (_selPhase != null)
            foreach (var s in _selPhase.Spawns)
            {
                var p = WorldToScreen(s.At);
                if (s.TriggerRadiusM > 0) { var r = WorldToPixels(s.At, s.TriggerRadiusM * 100); dc.DrawEllipse(null, TriggerRing, p, r, r); }
                if (s.HoldRadiusM > 0) { var r = WorldToPixels(s.At, s.HoldRadiusM * 100); dc.DrawEllipse(null, HoldRing, p, r, r); }
            }

        for (var i = 0; i < _mission.Phases.Count; i++)
        {
            var phase = _mission.Phases[i];
            if (phase.Center is not { } c) continue;
            var selected = phase == _selPhase;
            var p = WorldToScreen(c);
            var size = selected ? 12 : 9;
            dc.DrawEllipse(selected ? PhaseBadge : PhaseBadgeDim, SelectedPin, p, size, size);
            var number = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                GridFace, selected ? 14 : 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(number, new Point(p.X - number.Width / 2, p.Y - number.Height / 2));
            if (selected || _zoom >= 3) Label(dc, phase.Name, p, size);
        }

        if (_selPhase != null)
            foreach (var s in _selPhase.Spawns)
            {
                var p = WorldToScreen(s.At);
                var fill = s.State switch
                {
                    MissionSpawnState.Spawned => PinSpawned,
                    MissionSpawnState.Partial => PinPartial,
                    MissionSpawnState.Refused => PinRefused,
                    MissionSpawnState.Sent => PinSent,
                    _ => PinPending,
                };
                // Shape says what it is (vehicle, item or kit, creature), colour says what happened to it.
                var selected = s == _selSpawn;
                var pen = selected ? SelectedPin : MarkerOutline;
                if (s.Kind == "vehicle") dc.DrawRoundedRectangle(fill, pen, new Rect(p.X - 8, p.Y - 5, 16, 10), 2, 2);
                else if (s.Kind is "item" or "kit") dc.DrawRectangle(fill, pen, new Rect(p.X - 5, p.Y - 5, 10, 10));
                else dc.DrawEllipse(fill, pen, p, selected ? 7 : 5, selected ? 7 : 5);
                if (selected || _zoom >= 5) Label(dc, s.Summary, p, 8);
            }
    }

    // ---------------------------------------------------------------- map-features overlays

    /// <summary>The island's threat zones (ellipses) and hunting areas (circles), drawn under the markers.</summary>
    private void DrawFeatures(DrawingContext dc)
    {
        if (_projection == null || _features == null) return;
        var labels = _zoom >= 4.5;

        if (_showThreat)
            foreach (var z in _features.ThreatZones)
            {
                var at = new Vec3(z.X, z.Y, 0);
                var p = WorldToScreen(at);
                double rx = WorldToPixels(at, z.Rx), ry = WorldToPixels(at, z.Ry);
                if (Offscreen(p, Math.Max(rx, ry))) continue;
                var (pen, fill) = z.Tier switch
                {
                    ThreatTier.High => ThreatHigh,
                    ThreatTier.Medium => ThreatMedium,
                    ThreatTier.Low => ThreatLow,
                    _ => ThreatNone,
                };
                if (Math.Abs(z.Yaw) > 0.5 && Math.Abs(rx - ry) > 1)
                {
                    dc.PushTransform(new RotateTransform(-z.Yaw, p.X, p.Y));
                    dc.DrawEllipse(fill, pen, p, rx, ry);
                    dc.Pop();
                }
                else dc.DrawEllipse(fill, pen, p, rx, ry);
                if (labels) Label(dc, Pretty(z.Settings), p, 4);
            }

        if (_showHunting)
            foreach (var h in _features.HuntingAreas)
            {
                var at = new Vec3(h.X, h.Y, 0);
                var p = WorldToScreen(at);
                var r = WorldToPixels(at, h.R);
                if (Offscreen(p, r)) continue;
                var (pen, fill) = h.IsMutantDen ? HuntingDen : HuntingGame;
                dc.DrawEllipse(fill, pen, p, r, r);
                if (labels) Label(dc, Pretty(h.Biome), p, 4);
            }
    }

    private bool Offscreen(Point p, double r) =>
        p.X + r < 0 || p.X - r > ActualWidth || p.Y + r < 0 || p.Y - r > ActualHeight;

    /// <summary>A readable label from a settings/biome asset name: drop the HTZ_/MTZ_/LTZ_/BD_ prefix, underscores to spaces.</summary>
    private static string Pretty(string name)
    {
        foreach (var prefix in new[] { "HTZ_", "MTZ_", "LTZ_", "BD_" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name[prefix.Length..].Replace('_', ' ');
        return name.Replace('_', ' ');
    }

    /// <summary>Base-game sector grid, optional keypad sub-grid, and labels in each cell's top-left corner.</summary>
    private void DrawGrid(DrawingContext dc)
    {
        if (_grid == null || _projection == null || _gridStyle == GridStyle.Off) return;
        const int fine = SectorGrid.Sectors * SectorGrid.KeypadDivisions;
        var cellW = _grid.SectorWidth / SectorGrid.KeypadDivisions;
        var cellH = _grid.SectorHeight / SectorGrid.KeypadDivisions;
        var mapW = _projection.MapWidth;
        var mapH = _projection.MapHeight;

        var keypadLines = _gridStyle is not GridStyle.Sectors;
        var sectorLines = _gridStyle is GridStyle.Sectors or GridStyle.SectorsAndKeypads or GridStyle.Keypads;
        for (var i = 1; i < fine; i++)
        {
            var isSector = i % SectorGrid.KeypadDivisions == 0;
            Pen? pen = isSector && sectorLines ? SectorPen : keypadLines ? KeypadPen : null;
            if (pen == null) continue;
            dc.DrawLine(pen, ToScreen(i * cellW, 0), ToScreen(i * cellW, mapH));
            dc.DrawLine(pen, ToScreen(0, i * cellH), ToScreen(mapW, i * cellH));
        }

        var cellPx = cellW * Scale;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        switch (_gridStyle)
        {
            case GridStyle.Sectors or GridStyle.SectorsAndKeypads:
                var size = Math.Clamp(cellPx * 0.45, 12, 30);
                for (var row = 0; row < SectorGrid.Sectors; row++)
                for (var col = 0; col < SectorGrid.Sectors; col++)
                {
                    var name = new SectorGrid.Cell(col, row, 0, 0).Sector;
                    GridLabel(dc, name, ToScreen(col * _grid.SectorWidth, row * _grid.SectorHeight), size, dpi);
                }
                break;

            case GridStyle.Keypads or GridStyle.MapKeypads or GridStyle.MapKeypadsInverted:
                if (cellPx < 30) break; // too dense to read; zoom in
                var small = Math.Clamp(cellPx * 0.16, 10, 16);
                for (var fy = 0; fy < fine; fy++)
                for (var fx = 0; fx < fine; fx++)
                {
                    var name = _gridStyle == GridStyle.Keypads
                        ? new SectorGrid.Cell(fx / 3, fy / 3, fx % 3, fy % 3).Keypad
                        : SectorGrid.MapKeypadName(fx, fy, _gridStyle == GridStyle.MapKeypadsInverted);
                    GridLabel(dc, name, ToScreen(fx * cellW, fy * cellH), small, dpi);
                }
                break;
        }
    }

    private void GridLabel(DrawingContext dc, string text, Point topLeft, double size, double dpi)
    {
        if (topLeft.X > ActualWidth || topLeft.Y > ActualHeight || topLeft.X < -200 || topLeft.Y < -100) return;
        var halo = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GridFace, size, GridHalo, dpi);
        var front = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GridFace, size, GridText, dpi);
        var at = new Point(topLeft.X + 4, topLeft.Y + 2);
        // A cheap outline: the dark copy offset in four directions keeps labels readable on any terrain.
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }) dc.DrawText(halo, new Point(at.X + dx, at.Y + dy));
        dc.DrawText(front, at);
    }

    private void Label(DrawingContext dc, string text, Point at, double offset)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 11, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var origin = new Point(at.X + offset + 3, at.Y - ft.Height / 2);
        dc.DrawRoundedRectangle(LabelBack, null, new Rect(origin.X - 3, origin.Y - 1, ft.Width + 6, ft.Height + 2), 3, 3);
        dc.DrawText(ft, origin);
    }

    private Brush BrushFor(string hex)
    {
        if (_brushes.TryGetValue(hex, out var b)) return b;
        try { b = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))); }
        catch (FormatException) { b = Brushes.Gray; }
        _brushes[hex] = b;
        return b;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    // ---------------------------------------------------------------- input

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        var (mx, my) = ToMapUnits(p);
        _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 0.5 : -0.5), 0, 8);
        // Keep the point under the cursor fixed while zooming.
        _cx = mx - (p.X - ActualWidth / 2) / Scale;
        _cy = my - (p.Y - ActualHeight / 2) / Scale;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _dragStart = e.GetPosition(this);
        _dragCenter = (_cx, _cy);
        _dragged = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_dragStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            var d = p - start;
            if (Math.Abs(d.X) + Math.Abs(d.Y) > 3) _dragged = true;
            _cx = _dragCenter.X - d.X / Scale;
            _cy = _dragCenter.Y - d.Y / Scale;
            InvalidateVisual();
        }
        Hover?.Invoke(WorldAt(p), MarkerNear(p));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        var p = e.GetPosition(this);
        if (_dragStart != null && !_dragged && WorldAt(p) is { } w) Clicked?.Invoke(w, MarkerNear(p));
        _dragStart = null;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (WorldAt(p) is { } w) ContextRequested?.Invoke(w, MarkerNear(p));
    }

    protected override void OnMouseLeave(MouseEventArgs e) => Hover?.Invoke(null, null);

    private Vec3? WorldAt(Point p)
    {
        if (_projection == null) return null;
        var (mx, my) = ToMapUnits(p);
        return _projection.Contains(mx, my) ? _projection.ToWorld(mx, my) : null;
    }

    /// <summary>The visible marker within a few pixels of the point, if any.</summary>
    private MapMarker? MarkerNear(Point p)
    {
        MapMarker? best = null;
        var bestD = 9.0 * 9.0;
        foreach (var m in _markers)
        {
            var s = ToScreen(m.MapX, m.MapY);
            var d = (s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }
}
