using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using Sacc.App.Infrastructure;
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
        EnterClipboard();
        string? saved = settings.RestoreClipboard && settings.InputMode == InputMode.Paste ? TryGetClipboardText() : null;
        try
        {
            var game = RequireGame(settings);
            await SendBatchAsync(settings, game, lines, ct);
        }
        finally
        {
            if (saved != null) SetClipboardText(saved);
            _gate.Release();
            LeaveClipboard();
            if (settings.ReturnFocusToSacc && returnTo != IntPtr.Zero) SetForegroundWindow(returnTo);
        }
    }

    /// <summary>
    /// Sends lines one at a time in one chat session. After each one, <paramref name="stopAfter"/> decides
    /// whether to stop (e.g. the game accepted it). Returns the index it stopped at, or -1 if it sent them all.
    /// </summary>
    public async Task<int> SendUntilAsync(Settings settings, IReadOnlyList<string> lines, Func<int, CancellationToken, Task<bool>> stopAfter,
                                          IntPtr returnTo, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        EnterClipboard();
        string? saved = settings.RestoreClipboard && settings.InputMode == InputMode.Paste ? TryGetClipboardText() : null;
        try
        {
            var game = RequireGame(settings);
            return await SendBatchAsync(settings, game, lines, ct, stopAfter: stopAfter);
        }
        finally
        {
            if (saved != null) SetClipboardText(saved);
            _gate.Release();
            LeaveClipboard();
            if (settings.ReturnFocusToSacc && returnTo != IntPtr.Zero) SetForegroundWindow(returnTo);
        }
    }

    /// <summary>Send one line and wait for the game to place its output on the clipboard.</summary>
    /// <param name="alwaysReturn">Go back to <paramref name="returnTo"/> even when the Return focus setting is off (unattended reads).</param>
    public async Task<string?> SendAndCaptureAsync(Settings settings, string line, IntPtr returnTo, CancellationToken ct, bool alwaysReturn = false)
    {
        await _gate.WaitAsync(ct);
        EnterClipboard();
        string? saved = settings.RestoreClipboard ? TryGetClipboardText() : null;
        try
        {
            var game = RequireGame(settings);
            var sentinel = "sacc-wait-" + Guid.NewGuid().ToString("N");
            SetClipboardText(sentinel);
            // Paste mode would overwrite the sentinel with the command itself, so
            // re-arm it right after the line is entered.
            await SendBatchAsync(settings, game, new[] { line }, ct, afterEnter: () => SetClipboardText(sentinel));

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
            LeaveClipboard();
            if (alwaysReturn && returnTo != IntPtr.Zero) RequestForeground(returnTo);
            else if (settings.ReturnFocusToSacc && returnTo != IntPtr.Zero) SetForegroundWindow(returnTo);
        }
    }

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    private GameStatus RequireGame(Settings settings)
    {
        var game = Probe(settings.ProcessName);
        if (!game.Found)
            throw new InvalidOperationException($"No '{settings.ProcessName}' window found. Is the game running?");
        if (InputBlockedByElevation(game))
            throw new InvalidOperationException("The game appears to be running as administrator, so Windows blocks SACC's input. Restart SACC as administrator (Settings tab).");
        return game;
    }

    /// <summary>
    /// One chat session per batch. SCUM's chat stays open after Enter, so pressing the
    /// open-chat key again would type it into the box ("t#SpawnItem …" as plain chat).
    /// Instead: open chat once, select-all + enter each line, and Escape at the end.
    /// </summary>
    private async Task<int> SendBatchAsync(Settings s, GameStatus game, IReadOnlyList<string> lines, CancellationToken ct, Action? afterEnter = null,
                                           Func<int, CancellationToken, Task<bool>>? stopAfter = null)
    {
        if (!await FocusAsync(game, ct))
        {
            FileLog.Write("WARN", $"Focus failed: target pid {game.ProcessId} hwnd 0x{game.Window.ToInt64():X}; foreground is {DescribeForeground()}");
            throw new InvalidOperationException("Could not bring the game window to the front. Click into SCUM once, then try again from SACC.");
        }
        await Task.Delay(s.FocusDelayMs, ct);

        var chatKey = ChatKeyVk(s.ChatKey);
        var ctrl = KeyInterop.VirtualKeyFromKey(Key.LeftCtrl);
        var chatOpen = false;
        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!chatOpen)
                {
                    EnsureStillFocused(game);
                    Tap(chatKey);
                    chatOpen = true;
                    await Task.Delay(s.ChatOpenDelayMs, ct);
                }

                EnsureStillFocused(game);
                // Replace anything already in the box (a stray chat-key letter, half-typed text).
                Chord(ctrl, KeyInterop.VirtualKeyFromKey(Key.A));
                if (s.InputMode == InputMode.Paste)
                {
                    SetClipboardText(lines[i]);
                    Chord(ctrl, KeyInterop.VirtualKeyFromKey(Key.V));
                }
                else
                {
                    TypeText(lines[i]);
                }
                await Task.Delay(s.BeforeEnterDelayMs, ct);

                // Never press Enter in some other window the user switched to mid-send.
                EnsureStillFocused(game);
                Tap(KeyInterop.VirtualKeyFromKey(Key.Enter));
                afterEnter?.Invoke();
                if (!s.ChatStaysOpenAfterSend) chatOpen = false;
                await Task.Delay(s.AfterSendDelayMs, ct);
                if (stopAfter != null && await stopAfter(i, ct)) return i;
            }
            return -1;
        }
        finally
        {
            // Close chat so the player isn't left typing into it; only while the game still has focus,
            // since Escape in another window would act on that window instead.
            if (chatOpen && s.ChatStaysOpenAfterSend && IsGameForeground(game))
            {
                Tap(KeyInterop.VirtualKeyFromKey(Key.Escape));
                await Task.Delay(s.CloseDelayMs, CancellationToken.None);
            }
        }
    }

    private static void EnsureStillFocused(GameStatus game)
    {
        if (!IsGameForeground(game))
            throw new OperationCanceledException("Game lost focus; stopped before sending.");
    }

    // Compare by process: a fullscreen game can activate a different window of its own than MainWindowHandle.
    private static bool IsGameForeground(GameStatus game)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)game.ProcessId;
    }

    /// <summary>
    /// Bring the game forward and wait for Windows to actually switch: fullscreen games
    /// activate asynchronously, so an immediate check after SetForegroundWindow can fail.
    /// The second attempt taps Alt first, which lifts Windows' foreground lock.
    /// </summary>
    private static async Task<bool> FocusAsync(GameStatus game, CancellationToken ct)
    {
        if (IsGameForeground(game)) return true;
        if (IsIconic(game.Window)) ShowWindow(game.Window, SW_RESTORE);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1) Tap(KeyInterop.VirtualKeyFromKey(Key.LeftAlt));
            RequestForeground(game.Window);
            for (int waited = 0; waited < 1500; waited += 50)
            {
                if (IsGameForeground(game)) return true;
                await Task.Delay(50, ct);
            }
        }
        return false;
    }

    private static void RequestForeground(IntPtr hwnd)
    {
        var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
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
    }

    private static string DescribeForeground()
    {
        var fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return $"pid {pid} ({p.ProcessName}) hwnd 0x{fg.ToInt64():X}";
        }
        catch (ArgumentException) { return $"pid {pid} hwnd 0x{fg.ToInt64():X}"; }
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

    /// <summary>The last text SACC itself put on the clipboard, so the position watcher can ignore its own writes.</summary>
    public static string? LastSelfWrite { get; private set; }

    private static int _clipboardBusy;
    /// <summary>True while SACC is driving the clipboard for a send or capture; the Ctrl+C position watcher pauses then.</summary>
    public static bool ClipboardBusy => Volatile.Read(ref _clipboardBusy) > 0;
    private static void EnterClipboard() => Interlocked.Increment(ref _clipboardBusy);
    private static void LeaveClipboard() => Interlocked.Decrement(ref _clipboardBusy);

    public static void SetClipboardText(string text)
    {
        LastSelfWrite = text;
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
