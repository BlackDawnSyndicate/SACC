using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Sacc.App.Infrastructure;

/// <summary>
/// Raises <see cref="Changed"/> whenever the Windows clipboard changes, using a message-only window and the
/// clipboard-format-listener API (no polling). Used by the position minimap to notice SCUM's Ctrl+C copies.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private HwndSource? _source;

    /// <summary>Fires on the UI thread each time the clipboard contents change (from any app, including SACC itself).</summary>
    public event Action? Changed;

    public ClipboardMonitor()
    {
        var p = new HwndSourceParameters("SACC.ClipboardMonitor")
        {
            ParentWindow = HWND_MESSAGE, // message-only window: receives messages, never shown
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) Changed?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
