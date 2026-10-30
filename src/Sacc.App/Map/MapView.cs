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

    public bool ShowPlayers { get => _showPlayers; set { _showPlayers = value; InvalidateVisual(); } }
    private bool _showPlayers = true;
    public bool ShowBookmarks { get => _showBookmarks; set { _showBookmarks = value; InvalidateVisual(); } }
    private bool _showBookmarks = true;

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

        if (_showPlayers && _players != null)
            foreach (var pl in _players)
            {
                if (pl.Location is not { } loc) continue;
                var (mx, my) = _projection.ToMap(loc.X, loc.Y);
                var p = ToScreen(mx, my);
                dc.DrawEllipse(PlayerFill, MarkerOutline, p, 6, 6);
                Label(dc, string.IsNullOrWhiteSpace(pl.Nickname) ? pl.SteamId : pl.Nickname, p, 6);
            }
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
