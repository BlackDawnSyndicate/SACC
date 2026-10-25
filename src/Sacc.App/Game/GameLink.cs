using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using Sacc.Core.Model;
using InputMode = Sacc.Core.Model.InputMode;
using static Sacc.App.Game.Native;

namespace Sacc.App.Game;

public sealed record GameStatus(bool Found, IntPtr Window, int ProcessId, bool? Elevated)
{
    public static readonly GameStatus Missing = new(false, IntPtr.Zero, 0, null);
}

/// <summary>
/// Delivers admin commands to SCUM the way a person would: focus the game,
/// open chat, paste or type the line, press Enter. Nothing is injected into
/// the game process; it only receives ordinary keyboard input.
/// </summary>
public sealed class GameLink
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static bool SelfElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public GameStatus Probe(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p)
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                return new GameStatus(true, p.MainWindowHandle, p.Id, IsElevated(p.Id));
            }
        }
        return GameStatus.Missing;
    }

    /// <summary>
    /// Windows silently drops input sent from a normal process to an elevated one,
    /// so an elevated game with a non-elevated SACC never receives anything.
    /// </summary>
    public static bool InputBlockedByElevation(GameStatus s) => !SelfElevated && s.Elevated != false;

    public async Task SendAsync(Settings settings, IReadOnlyList<string> lines, IntPtr returnTo, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var game = RequireGame(settings);
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                await SendLineAsync(settings, game, line, ct);
            }
        }
        finally
        {
            _gate.Release();
            if (settings.ReturnFocusToSacc && returnTo != IntPtr.Zero) SetForegroundWindow(returnTo);
        }
    }

    /// <summary>Send one line and wait for the game to place its output on the clipboard.</summary>
    public async Task<string?> SendAndCaptureAsync(Settings settings, string line, IntPtr returnTo, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string? saved = settings.RestoreClipboard ? TryGetClipboardText() : null;
        try
        {
            var game = RequireGame(settings);
            var sentinel = "sacc-wait-" + Guid.NewGuid().ToString("N");
            SetClipboardText(sentinel);
            // Paste mode would overwrite the sentinel with the command itself, so
            // re-arm it right after the line is entered.
            await SendLineAsync(settings, game, line, ct, afterEnter: () => SetClipboardText(sentinel));

            var deadline = DateTime.UtcNow.AddMilliseconds(settings.ClipboardCaptureTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
                var now = TryGetClipboardText();
                if (now != null && now != sentinel) return now;
            }
            return null;
        }
        finally
        {
            if (saved != null) SetClipboardText(saved);
            _gate.Release();
            if (settings.ReturnFocusToSacc && returnTo != IntPtr.Zero) SetForegroundWindow(returnTo);
        }
    }

    private GameStatus RequireGame(Settings settings)
    {
        var game = Probe(settings.ProcessName);
        if (!game.Found)
            throw new InvalidOperationException($"No '{settings.ProcessName}' window found. Is the game running?");
        if (InputBlockedByElevation(game))
            throw new InvalidOperationException("The game appears to be running as administrator, so Windows blocks SACC's input. Restart SACC as administrator (Settings tab).");
        return game;
    }

    private async Task SendLineAsync(Settings s, GameStatus game, string line, CancellationToken ct, Action? afterEnter = null)
    {
        if (!Focus(game.Window))
            throw new InvalidOperationException("Could not bring the game window to the front.");
        await Task.Delay(s.FocusDelayMs, ct);

        EnsureStillFocused(game);
        Tap(ChatKeyVk(s.ChatKey));
        await Task.Delay(s.ChatOpenDelayMs, ct);

        string? saved = null;
        EnsureStillFocused(game);
        if (s.InputMode == InputMode.Paste)
        {
            if (s.RestoreClipboard && afterEnter == null) saved = TryGetClipboardText();
            SetClipboardText(line);
            Chord(KeyInterop.VirtualKeyFromKey(Key.LeftCtrl), KeyInterop.VirtualKeyFromKey(Key.V));
        }
        else
        {
            TypeText(line);
        }
        await Task.Delay(s.BeforeEnterDelayMs, ct);

        // Never press Enter in some other window the user switched to mid-send.
        EnsureStillFocused(game);
        Tap(KeyInterop.VirtualKeyFromKey(Key.Enter));
        afterEnter?.Invoke();
        await Task.Delay(s.AfterSendDelayMs, ct);

        if (saved != null) SetClipboardText(saved);
    }

    private static void EnsureStillFocused(GameStatus game)
    {
        if (GetForegroundWindow() != game.Window)
            throw new OperationCanceledException("Game lost focus; stopped before sending.");
    }

    private static bool Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        var fg = GetForegroundWindow();
        if (fg == hwnd) return true;
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var me = GetCurrentThreadId();
        var attached = fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
        return GetForegroundWindow() == hwnd;
    }

    public static int ChatKeyVk(string name)
    {
        if (!Enum.TryParse<Key>(name, ignoreCase: true, out var key) || key == Key.None)
            throw new InvalidOperationException($"Chat key '{name}' is not a recognised key name (e.g. T, Enter, Oem2).");
        return KeyInterop.VirtualKeyFromKey(key);
    }

    // Scan codes rather than bare virtual keys: games that read raw input ignore VK-only events.
    private static INPUT KeyEvent(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wScan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC),
                dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0),
            },
        },
    };

    private static void Tap(int vk) => Send(KeyEvent(vk, false), KeyEvent(vk, true));

    private static void Chord(int modifier, int vk) =>
        Send(KeyEvent(modifier, false), KeyEvent(vk, false), KeyEvent(vk, true), KeyEvent(modifier, true));

    private static void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            foreach (var up in new[] { false, true })
            {
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
                });
            }
        }
        Send(inputs.ToArray());
    }

    private static void Send(params INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"Windows accepted {sent} of {inputs.Length} key events (error {Marshal.GetLastWin32Error()}).");
    }

    public static string? TryGetClipboardText()
    {
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (COMException) { Thread.Sleep(30); }
        }
        return null;
    }

    public static void SetClipboardText(string text)
    {
        for (int i = 0; ; i++)
        {
            // SetDataObject with copy=true survives SACC exiting; SetText can race other clipboard owners.
            try { Clipboard.SetDataObject(text, true); return; }
            catch (COMException) when (i < 5) { Thread.Sleep(30); }
        }
    }

    private static bool? IsElevated(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) ? elevated != 0 : null;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
}
