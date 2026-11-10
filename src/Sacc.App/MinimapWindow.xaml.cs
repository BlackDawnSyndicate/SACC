using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using Sacc.App.Map;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App;

/// <summary>
/// A small always-on-top overlay that shows the player's last copied position on the island map.
/// It never takes focus (so it can't steal keystrokes from the game) and is driven by the main window,
/// which feeds it a position each time SCUM copies coordinates with Ctrl+C.
/// </summary>
public partial class MinimapWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private readonly DispatcherTimer _fade;

    public MinimapWindow()
    {
        InitializeComponent();
        _fade = new DispatcherTimer();
        _fade.Tick += (_, _) => StartFade();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never activate (so the game keeps focus) and stay out of the taskbar / alt-tab.
        var h = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public bool HasMap { get; private set; }

    /// <summary>Point the minimap at the loaded map tiles (call once the map is ready).</summary>
    public void SetMap(MapTemplate template, string tileTemplate, MapDataService data)
    {
        MiniMap.Data = data;
        MiniMap.SetMap(template, tileTemplate);
        HasMap = true;
    }

    /// <summary>Move the marker to a world position and recentre on it.</summary>
    public void MoveTo(Vec3 world, double zoom)
    {
        MiniMap.SelfPosition = world;
        MiniMap.CenterOn(world.X, world.Y, zoom);
    }

    /// <summary>Always-on mode: show and stay (no fade).</summary>
    public void ShowSteady()
    {
        _fade.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
    }

    /// <summary>On-copy mode: show at full opacity, then fade out after <paramref name="seconds"/> (reset on each call).</summary>
    public void Pulse(double seconds)
    {
        _fade.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
        _fade.Interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        _fade.Start();
    }

    public void HideNow()
    {
        _fade.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Hide();
    }

    private void StartFade()
    {
        _fade.Stop();
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.2));
        anim.Completed += (_, _) => { if (Opacity < 0.05) Hide(); };
        BeginAnimation(OpacityProperty, anim);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => OnHidden?.Invoke();

    /// <summary>Raised when the user clicks the ✕; the owner turns the minimap setting off.</summary>
    public event Action? OnHidden;

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }
}
