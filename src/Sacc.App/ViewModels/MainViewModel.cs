using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Sacc.App.Game;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;
using InputMode = Sacc.Core.Model.InputMode;

namespace Sacc.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GameLink _game = new();
    private readonly Profile _profile;
    private readonly Func<string, bool> _confirm;
    private readonly Func<IntPtr> _ownWindow;
    private readonly DispatcherTimer _probeTimer;
    private CancellationTokenSource? _cts;
    private CommandCatalog _catalog = new();

    public MainViewModel(Func<string, bool> confirm, Func<IntPtr> ownWindow)
    {
        _confirm = confirm;
        _ownWindow = ownWindow;
        _profile = LoadProfile();

        Players = new(_profile.Players);
        Flags = new(_profile.Flags);
        // A separate view: the Map tab binds Players directly and must stay unfiltered.
        PlayersView = new ListCollectionView(Players) { Filter = o => PlayerVisible((PlayerRecord)o) };
        Bookmarks = new(_profile.Bookmarks);
        Macros = new(_profile.Macros.Select(m => new MacroViewModel(m)));
        if (Macros.Count == 0) Macros.Add(new MacroViewModel(SampleMacro));
        FavoriteItems = new(_profile.FavoriteItems);
        FavoriteVehicles = new(_profile.FavoriteVehicles);
        History = new(_profile.History);
        InitKits();
        RebuildItemCatalog();

        CommandsView = new ListCollectionView(new List<CommandDef>());
        LoadCatalog();

        SendCommand = new AsyncCommand(SendSelectedAsync, () => SelectedCommand != null);
        CopyCommand = new RelayCommand(() => { if (BuildPreview() is { Ok: true } r) GameLink.SetClipboardText(r.Text); }, () => SelectedCommand != null);
        SendFreeformCommand = new AsyncCommand(SendFreeformAsync);
        StopCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);

        RefreshPlayersCommand = new AsyncCommand(RefreshPlayersAsync);
        PastePlayersCommand = new RelayCommand(PastePlayers);
        ClearPlayersCommand = new RelayCommand(() => { Players.Clear(); _profile.PlayersListedAt = null; RefreshPlayersView(); Save(); });
        PlayerActionCommand = new AsyncCommand(PlayerActionAsync, _ => SelectedPlayer != null);
        UsePlayerAsTargetCommand = new RelayCommand(() => { BookmarkTarget = MacroTarget = SelectedPlayer?.SteamId ?? ""; }, () => SelectedPlayer != null);

        AddBookmarkCommand = new RelayCommand(() => { Bookmarks.Add(new Bookmark { Name = "New location" }); Save(); });
        DeleteBookmarkCommand = new RelayCommand(() => { if (SelectedBookmark != null) { Bookmarks.Remove(SelectedBookmark); Save(); } }, () => SelectedBookmark != null);
        TeleportBookmarkCommand = new AsyncCommand(TeleportBookmarkAsync, () => SelectedBookmark != null);
        CaptureLocationCommand = new AsyncCommand(CaptureLocationAsync);

        NewMacroCommand = new RelayCommand(() => { var m = new MacroViewModel(new Macro()); Macros.Add(m); SelectedMacro = m; });
        DeleteMacroCommand = new RelayCommand(() => { if (SelectedMacro != null && _confirm($"Delete macro '{SelectedMacro.Name}'?")) { Macros.Remove(SelectedMacro); Save(); } }, () => SelectedMacro != null);
        CheckMacroCommand = new RelayCommand(() => ReportMacro(ParseMacro()), () => SelectedMacro != null);
        RunMacroCommand = new AsyncCommand(RunMacroAsync, () => SelectedMacro != null);

        SpawnItemCommand = new AsyncCommand(o => RunCatalogAsync("spawn-item", new()
        {
            ["item"] = o as string ?? ItemCode,
            ["amount"] = ItemAmount,
            ["stack"] = SpawnerStack(o as string ?? ItemCode),
        }));
        SpawnVehicleCommand = new AsyncCommand(o => RunCatalogAsync("spawn-vehicle", new() { ["vehicle"] = o as string ?? VehicleCode }));
        ListItemsCommand = new AsyncCommand(() => RunCatalogAsync("list-items", new() { ["filter"] = ItemCode }));
        AddFavoriteItemCommand = new RelayCommand(() => AddFavorite(FavoriteItems, ItemCode));
        RemoveFavoriteItemCommand = new RelayCommand(o => { if (o is string s) { FavoriteItems.Remove(s); Save(); } });
        AddFavoriteVehicleCommand = new RelayCommand(() => AddFavorite(FavoriteVehicles, VehicleCode));
        RemoveFavoriteVehicleCommand = new RelayCommand(o => { if (o is string s) { FavoriteVehicles.Remove(s); Save(); } });

        ResendCommand = new AsyncCommand(o => o is HistoryEntry h ? ExecuteAsync(new[] { h.Text }) : Task.CompletedTask);
        ToFreeformCommand = new RelayCommand(o => { if (o is HistoryEntry h) FreeformText = h.Text; });
        ClearHistoryCommand = new RelayCommand(() => { History.Clear(); Save(); });

        SaveSettingsCommand = new RelayCommand(() => { Save(); Status = "Settings saved."; });
        ReloadCatalogCommand = new RelayCommand(LoadCatalog);
        EditCatalogCommand = new RelayCommand(EditCatalog);
        OpenDataFolderCommand = new RelayCommand(() => { Directory.CreateDirectory(AppPaths.Root); Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true }); });
        RelaunchAsAdminCommand = new RelayCommand(RelaunchAsAdmin, () => !GameLink.SelfElevated);

        _probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _probeTimer.Tick += (_, _) => ProbeGame();
        _probeTimer.Start();
        ProbeGame();
    }

    private static readonly Macro SampleMacro = new()
    {
        Name = "Storm night (sample)",
        Script = "// One admin command per line. 'wait <ms>' pauses. {player} = target box, {me} = your SteamID.\n" +
                 "#Announce A storm is rolling in. Find shelter.\nwait 3000\n#SetTime 22\nwait 500\n#SetWeather 1",
    };

    // ---------------------------------------------------------------- state

    public Settings Settings => _profile.Settings;
    public ObservableCollection<PlayerRecord> Players { get; }
    public ObservableCollection<FlagRecord> Flags { get; }
    public ObservableCollection<Bookmark> Bookmarks { get; }
    public ObservableCollection<MacroViewModel> Macros { get; }
    public ObservableCollection<string> FavoriteItems { get; }
    public ObservableCollection<string> FavoriteVehicles { get; }
    public ObservableCollection<HistoryEntry> History { get; }
    public ObservableCollection<ParamFieldViewModel> Fields { get; } = new();
    public ListCollectionView CommandsView { get; private set; }

    public string AppTitle => $"SACC — SCUM Admin Command Console {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";
    public bool SelfElevated => GameLink.SelfElevated;
    public IEnumerable<InputMode> InputModes => Enum.GetValues<InputMode>();

    private string _status = "Ready.";
    public string Status { get => _status; set => Set(ref _status, value); }

    private string _gameStatus = "";
    public string GameStatusText { get => _gameStatus; private set => Set(ref _gameStatus, value); }

    private bool _gameOk;
    public bool GameOk { get => _gameOk; private set => Set(ref _gameOk, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    public bool DryRun
    {
        get => Settings.DryRun;
        set { Settings.DryRun = value; Raise(); Save(); }
    }

    public bool AlwaysOnTop
    {
        get => Settings.AlwaysOnTop;
        set { Settings.AlwaysOnTop = value; Raise(); Save(); }
    }

    private string _search = "";
    public string SearchText
    {
        get => _search;
        set { if (Set(ref _search, value)) CommandsView.Refresh(); }
    }

    private CommandDef? _selectedCommand;
    public CommandDef? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (!Set(ref _selectedCommand, value)) return;
            CommandManager.InvalidateRequerySuggested();
            Fields.Clear();
            if (value != null)
                foreach (var p in value.Params)
                    Fields.Add(new ParamFieldViewModel(p, UpdatePreview) { Value = DefaultFor(p) });
            UpdatePreview();
        }
    }

    private string _preview = "";
    public string Preview { get => _preview; private set => Set(ref _preview, value); }

    private string _previewProblem = "";
    public string PreviewProblem { get => _previewProblem; private set => Set(ref _previewProblem, value); }

    private string _freeform = "";
    public string FreeformText { get => _freeform; set => Set(ref _freeform, value); }

    private PlayerRecord? _selectedPlayer;
    public PlayerRecord? SelectedPlayer { get => _selectedPlayer; set { if (Set(ref _selectedPlayer, value)) CommandManager.InvalidateRequerySuggested(); } }

    private string _playerAmount = "";
    public string PlayerAmount { get => _playerAmount; set => Set(ref _playerAmount, value); }

    private string _playerCurrency = "Normal";
    public string PlayerCurrency { get => _playerCurrency; set => Set(ref _playerCurrency, value); }
    public string[] Currencies { get; } = { "Normal", "Gold" };

    private Bookmark? _selectedBookmark;
    public Bookmark? SelectedBookmark { get => _selectedBookmark; set { if (Set(ref _selectedBookmark, value)) CommandManager.InvalidateRequerySuggested(); } }

    private string _bookmarkTarget = "";
    public string BookmarkTarget { get => _bookmarkTarget; set => Set(ref _bookmarkTarget, value); }

    private MacroViewModel? _selectedMacro;
    public MacroViewModel? SelectedMacro { get => _selectedMacro; set { if (Set(ref _selectedMacro, value)) CommandManager.InvalidateRequerySuggested(); } }

    private string _macroTarget = "";
    public string MacroTarget { get => _macroTarget; set => Set(ref _macroTarget, value); }

    private string _itemCode = "";
    public string ItemCode { get => _itemCode; set { if (Set(ref _itemCode, value)) RefreshItemMatches(); } }

    private string _itemAmount = "1";
    public string ItemAmount { get => _itemAmount; set => Set(ref _itemAmount, value); }

    private string _itemStack = "";
    public string ItemStack { get => _itemStack; set => Set(ref _itemStack, value); }

    private bool _itemStackFull;
    public bool ItemStackFull { get => _itemStackFull; set => Set(ref _itemStackFull, value); }

    /// <summary>Full when ticked; else the typed StackCount; else Full for a magazine if "Fill magazines" is on.</summary>
    private string SpawnerStack(string code)
    {
        var full = Settings.FullStackCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ItemStackFull) return full;
        if (!string.IsNullOrWhiteSpace(ItemStack)) return ItemStack;
        return Settings.SpawnerFillMagazines && ItemCatalog.MagazineMatcher(Settings.MagazinePattern)(code.Trim()) ? full : "";
    }

    private string _vehicleCode = "";
    public string VehicleCode { get => _vehicleCode; set { if (Set(ref _vehicleCode, value)) RefreshVehicleMatches(); } }

    private int _historyCursor = -1;

    // ---------------------------------------------------------------- commands

    public ICommand SendCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand SendFreeformCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RefreshPlayersCommand { get; }
    public ICommand PastePlayersCommand { get; }
    public ICommand ClearPlayersCommand { get; }
    public ICommand PlayerActionCommand { get; }
    public ICommand UsePlayerAsTargetCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand DeleteBookmarkCommand { get; }
    public ICommand TeleportBookmarkCommand { get; }
    public ICommand CaptureLocationCommand { get; }
    public ICommand NewMacroCommand { get; }
    public ICommand DeleteMacroCommand { get; }
    public ICommand CheckMacroCommand { get; }
    public ICommand RunMacroCommand { get; }
    public ICommand SpawnItemCommand { get; }
    public ICommand SpawnVehicleCommand { get; }
    public ICommand ListItemsCommand { get; }
    public ICommand AddFavoriteItemCommand { get; }
    public ICommand RemoveFavoriteItemCommand { get; }
    public ICommand AddFavoriteVehicleCommand { get; }
    public ICommand RemoveFavoriteVehicleCommand { get; }
    public ICommand ResendCommand { get; }
    public ICommand ToFreeformCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ReloadCatalogCommand { get; }
    public ICommand EditCatalogCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand RelaunchAsAdminCommand { get; }

    // ---------------------------------------------------------------- catalog / console

    private void LoadCatalog()
    {
        try
        {
            _catalog = JsonStore.LoadCatalog(AppPaths.ActiveCatalog);
            Status = $"Loaded {_catalog.Commands.Count} commands from {AppPaths.ActiveCatalog}.";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            Status = $"Catalog failed to load: {ex.Message}";
            FileLog.Write("ERROR", ex.ToString());
        }

        CommandsView = new ListCollectionView(_catalog.Commands) { Filter = MatchesSearch };
        CommandsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandDef.Category)));
        Raise(nameof(CommandsView));
        SelectedCommand = null;
    }

    private bool MatchesSearch(object o)
    {
        if (string.IsNullOrWhiteSpace(_search)) return true;
        var c = (CommandDef)o;
        return c.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || c.Template.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || c.Description.Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    private string DefaultFor(ParamDef p) =>
        p.Kind == ParamKind.Player && SelectedPlayer != null ? SelectedPlayer.SteamId : p.Default ?? "";

    private TemplateRenderer.Result? BuildPreview() =>
        SelectedCommand == null ? null
        : TemplateRenderer.Render(SelectedCommand, Fields.ToDictionary(f => f.Def.Name, f => (string?)f.Value));

    private void UpdatePreview()
    {
        var r = BuildPreview();
        Preview = r?.Text ?? "";
        PreviewProblem = r == null ? "" : string.Join("  ", r.Errors);
    }

    private async Task SendSelectedAsync()
    {
        var r = BuildPreview();
        if (r == null) return;
        if (!r.Ok) { Status = string.Join("  ", r.Errors); return; }
        await RunDefAsync(SelectedCommand!, r.Text);
    }

    private async Task SendFreeformAsync()
    {
        var check = CommandLine.Check(FreeformText);
        if (!check.Ok) { Status = check.Error!; return; }
        if (check.Warning != null) Status = check.Warning;
        if (await ExecuteAsync(new[] { check.Text })) FreeformText = "";
        _historyCursor = -1;
    }

    /// <summary>Up/Down in the freeform box walks back through sent lines like a shell.</summary>
    public void RecallHistory(int direction)
    {
        if (History.Count == 0) return;
        _historyCursor = Math.Clamp(_historyCursor + direction, -1, History.Count - 1);
        FreeformText = _historyCursor < 0 ? "" : History[_historyCursor].Text;
    }

    public async Task RunCatalogAsync(string id, Dictionary<string, string?> values)
    {
        var def = _catalog.Find(id);
        if (def == null) { Status = $"The command catalog has no '{id}' entry."; return; }
        var r = TemplateRenderer.Render(def, values);
        if (!r.Ok) { Status = $"{def.Name}: {string.Join("  ", r.Errors)}"; return; }
        await RunDefAsync(def, r.Text);
    }

    private async Task RunDefAsync(CommandDef def, string text)
    {
        if (def.Dangerous && Settings.ConfirmDangerous && !_confirm($"Send this command?\n\n{text}\n\n{def.Description}"))
            return;
        if (def.CapturesClipboard)
        {
            var output = await CaptureAsync(text);
            if (output != null) Status = $"Clipboard received ({output.Length} chars).";
            return;
        }
        await ExecuteAsync(new[] { text });
    }

    // ---------------------------------------------------------------- delivery

    private async Task<bool> ExecuteAsync(IReadOnlyList<string> lines)
    {
        // Every send path (Console, Spawner, raw box, macros, kits) passes here, so large spawns split once.
        lines = lines.SelectMany(l => CommandLine.SplitSpawn(l, Settings.MaxSpawnPerCommand)).ToList();
        if (lines.Count == 0) return false;
        if (Settings.DryRun)
        {
            foreach (var l in lines) Record(l, "dry run");
            Status = $"Dry run: {lines.Count} line(s) logged, nothing sent. Turn off Dry run to send.";
            return true;
        }
        return await GuardedAsync(async ct =>
        {
            await _game.SendAsync(Settings, lines, _ownWindow(), ct);
            foreach (var l in lines) Record(l, "sent");
            Status = $"Sent {lines.Count} line(s).";
        }) ;
    }

    private async Task<string?> CaptureAsync(string line)
    {
        if (Settings.DryRun)
        {
            Record(line, "dry run");
            Status = "Dry run: this command reads the game's clipboard output, so it needs a live send.";
            return null;
        }
        string? output = null;
        await GuardedAsync(async ct =>
        {
            output = await _game.SendAndCaptureAsync(Settings, line, _ownWindow(), ct);
            Record(line, output == null ? "sent, no clipboard reply" : "sent, clipboard read");
            if (output == null)
                Status = "The game did not put anything on the clipboard. Copy the chat output yourself and use Paste list.";
        });
        return output;
    }

    private async Task<bool> GuardedAsync(Func<CancellationToken, Task> work)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            await work(_cts.Token);
            return true;
        }
        catch (OperationCanceledException ex)
        {
            Status = string.IsNullOrEmpty(ex.Message) || ex.Message.StartsWith("The operation") ? "Stopped." : ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = ex.Message;
            FileLog.Write("WARN", ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Record(string text, string status)
    {
        History.Insert(0, new HistoryEntry { Time = DateTime.Now, Text = text, Status = status });
        while (History.Count > Math.Max(10, Settings.HistoryLimit)) History.RemoveAt(History.Count - 1);
        FileLog.Write("CMD", $"{status}: {text}");
        Save();
    }

    private void ProbeGame()
    {
        var s = _game.Probe(Settings.ProcessName);
        if (!s.Found) { GameOk = false; GameStatusText = $"{Settings.ProcessName} not running"; return; }
        if (GameLink.InputBlockedByElevation(s)) { GameOk = false; GameStatusText = "Game is elevated: restart SACC as admin"; return; }
        GameOk = true;
        GameStatusText = $"{Settings.ProcessName} found (pid {s.ProcessId})";
    }

    // ---------------------------------------------------------------- players

    private async Task RefreshPlayersAsync()
    {
        var def = _catalog.Find("list-players-copy");
        if (def == null) { Status = "The command catalog has no 'list-players-copy' entry."; return; }
        var output = await CaptureAsync(def.Template);
        if (output != null) MergePlayers(output);
    }

    private void PastePlayers()
    {
        var text = GameLink.TryGetClipboardText();
        if (string.IsNullOrWhiteSpace(text)) { Status = "Clipboard is empty. Copy the #ListPlayers output first."; return; }
        MergePlayers(text);
    }

    private void MergePlayers(string text)
    {
        var parsed = Parsing.ParsePlayerList(text);
        if (parsed.Count == 0) { Status = "No SteamID64s found in that text."; return; }
        SahImport.MergePlayers(Players, parsed);
        // #ListPlayers lists who is online, so this batch is the new "online now" set.
        _profile.PlayersListedAt = parsed.Max(p => p.LastSeen);
        RefreshPlayersView();
        Status = $"Imported {parsed.Count} player(s).";
        Save();
    }

    public ListCollectionView PlayersView { get; }

    private bool _playersOnlineOnly = true;
    public bool PlayersOnlineOnly { get => _playersOnlineOnly; set { if (Set(ref _playersOnlineOnly, value)) RefreshPlayersView(); } }

    private string _playerSearch = "";
    public string PlayerSearch { get => _playerSearch; set { if (Set(ref _playerSearch, value ?? "")) RefreshPlayersView(); } }

    public string PlayersOnlineLabel
    {
        get
        {
            var at = _profile.PlayersListedAt;
            if (at == null) return "Online now (not listed yet)";
            var count = Players.Count(p => p.LastSeen >= at.Value);
            return $"Online now ({count}, listed {at.Value:MMM d HH:mm})";
        }
    }

    private bool PlayerVisible(PlayerRecord p)
    {
        if (_playersOnlineOnly && (_profile.PlayersListedAt is not { } at || p.LastSeen < at)) return false;
        if (_playerSearch.Length == 0) return true;
        return p.Nickname.Contains(_playerSearch, StringComparison.OrdinalIgnoreCase)
            || p.PlatformName.Contains(_playerSearch, StringComparison.OrdinalIgnoreCase)
            || p.SteamId.Contains(_playerSearch, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshPlayersView()
    {
        PlayersView.Refresh();
        Raise(nameof(PlayersOnlineLabel));
        Raise(nameof(MapOnlineLabel));
        Raise(nameof(MapOnlineTip));
        MapChanged?.Invoke();
    }

    private Task PlayerActionAsync(object? parameter)
    {
        if (SelectedPlayer == null || parameter is not string id) return Task.CompletedTask;
        return RunCatalogAsync(id, new()
        {
            ["player"] = SelectedPlayer.SteamId,
            ["amount"] = PlayerAmount,
            ["currency"] = PlayerCurrency,
        });
    }

    // ---------------------------------------------------------------- locations

    private Task TeleportBookmarkAsync()
    {
        var b = SelectedBookmark!;
        // No target means "me": #Teleport without a player argument moves the admin.
        var target = BookmarkTarget.Trim();
        return RunCatalogAsync("teleport", new()
        {
            ["player"] = target,
            ["x"] = b.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["y"] = b.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["z"] = b.Z.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    private async Task CaptureLocationAsync()
    {
        var target = string.IsNullOrWhiteSpace(BookmarkTarget) ? Settings.MySteamId : BookmarkTarget;
        if (string.IsNullOrWhiteSpace(target)) { Status = "Set a target player, or your own SteamID64 in Settings."; return; }

        string? output;
        if (Settings.DryRun)
        {
            // Without a live game, accept coordinates the user copied by hand.
            output = GameLink.TryGetClipboardText();
            Status = "Dry run: read coordinates from the current clipboard instead of the game.";
        }
        else
        {
            var def = _catalog.Find("location-copy");
            if (def == null) { Status = "The command catalog has no 'location-copy' entry."; return; }
            var r = TemplateRenderer.Render(def, new Dictionary<string, string?> { ["player"] = target });
            if (!r.Ok) { Status = string.Join("  ", r.Errors); return; }
            output = await CaptureAsync(r.Text);
        }

        if (!Parsing.TryParseLocation(output, out var v)) { Status = "Couldn't find X/Y/Z coordinates in the clipboard."; return; }
        var b = new Bookmark { Name = $"Captured {DateTime.Now:HH:mm:ss}", X = v.X, Y = v.Y, Z = v.Z };
        Bookmarks.Add(b);
        SelectedBookmark = b;
        Status = $"Saved location {v}.";
        Save();
    }

    // ---------------------------------------------------------------- macros

    private MacroScript.Result ParseMacro()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["player"] = MacroTarget,
            ["me"] = Settings.MySteamId,
        };
        return MacroScript.Parse(SelectedMacro?.Script ?? "", vars);
    }

    private void ReportMacro(MacroScript.Result r)
    {
        Status = r.Ok
            ? $"Macro OK: {r.Steps.OfType<MacroScript.SendStep>().Count()} command(s)."
            : string.Join("  ", r.Errors);
    }

    private async Task RunMacroAsync()
    {
        var r = ParseMacro();
        if (!r.Ok) { ReportMacro(r); return; }
        Save();
        var commands = r.Steps.OfType<MacroScript.SendStep>().Select(s => s.Text).ToList();
        if (!_confirm($"Run macro '{SelectedMacro!.Name}' ({commands.Count} command(s))?\n\n{string.Join("\n", commands)}"))
            return;

        int sent = 0;
        foreach (var step in r.Steps)
        {
            if (step is MacroScript.WaitStep w)
            {
                if (!Settings.DryRun) await Task.Delay(w.Milliseconds);
                continue;
            }
            if (!await ExecuteAsync(new[] { ((MacroScript.SendStep)step).Text }))
            {
                Status = $"Macro stopped at line {step.Line}. {Status}";
                return;
            }
            sent++;
        }
        Status = $"Macro '{SelectedMacro.Name}' finished: {sent} command(s){(Settings.DryRun ? " (dry run)" : "")}.";
    }

    // ---------------------------------------------------------------- misc

    private void AddFavorite(ObservableCollection<string> list, string code)
    {
        code = code.Trim();
        if (code.Length == 0 || code.Contains(' ') || list.Contains(code, StringComparer.OrdinalIgnoreCase)) return;
        list.Add(code);
        RebuildItemCatalog();
        Save();
    }

    private void EditCatalog()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            if (!File.Exists(AppPaths.UserCatalog)) File.Copy(AppPaths.BundledCatalog, AppPaths.UserCatalog);
            Process.Start(new ProcessStartInfo(AppPaths.UserCatalog) { UseShellExecute = true });
            Status = "Editing your catalog copy. Save it, then press Reload catalog.";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Status = $"Couldn't open the catalog: {ex.Message}";
        }
    }

    private void RelaunchAsAdmin()
    {
        try
        {
            Save();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Status = "Restart as administrator was cancelled.";
        }
    }

    private static Profile LoadProfile()
    {
        try
        {
            var profile = JsonStore.LoadOrDefault<Profile>(AppPaths.Profile);
            profile.Migrate();
            return profile;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // Keep the unreadable file for the user rather than silently overwriting it.
            var aside = AppPaths.Profile + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            try { File.Move(AppPaths.Profile, aside); } catch (IOException) { }
            FileLog.Write("ERROR", $"Profile unreadable, moved to {aside}: {ex.Message}");
            return new Profile();
        }
    }

    public void Save()
    {
        _profile.Players = Players.ToList();
        _profile.Flags = Flags.ToList();
        _profile.Bookmarks = Bookmarks.ToList();
        _profile.Macros = Macros.Select(m => m.ToModel()).ToList();
        _profile.Kits = Kits.ToList();
        _profile.FavoriteItems = FavoriteItems.ToList();
        _profile.FavoriteVehicles = FavoriteVehicles.ToList();
        _profile.History = History.ToList();
        try { JsonStore.Save(AppPaths.Profile, _profile); }
        catch (IOException ex) { Status = $"Couldn't save profile: {ex.Message}"; }
    }
}
