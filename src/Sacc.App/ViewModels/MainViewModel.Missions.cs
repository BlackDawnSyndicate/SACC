using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Sacc.App.Game;
using Sacc.App.Infrastructure;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.App.ViewModels;

public sealed record MissionKind(string Key, string Label);

public enum MissionTool { Select, Centre, Spawn, Prefab, Move }

/// <summary>
/// The Mission tab: plan phases and preplaced spawns on a map, run them, count what really spawned, clean up, and
/// export. Plain missions need no player positions; pins with radius rules read #ListPlayers while a phase runs.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MinMissionPollSeconds = 5;
    private const double PinHitPixels = 12;

    /// <summary>Items aren't a GM panel action, but a mission can preplace them.</summary>
    private static readonly GmSpawn.Action ItemAction = new("item", "Item", "spawn-item-at-coords", "item", "amount");

    private readonly MissionSpawn _spawnDefaults = new() { Kind = "zombies", Type = "BP_Zombie_Military_Armored", Count = 5 };
    private DispatcherTimer? _missionTimer;
    private MissionPhase? _runPhase;
    private bool _missionBusy;
    private int _missionMisses;
    private ICommand? _newMission, _deleteMission, _addPhase, _removePhase, _phaseUp, _phaseDown, _prevPhase, _nextPhase, _removeSpawn,
        _runPhaseCmd, _stopRun, _cleanupPhase, _resetRun, _copyMissionScript, _saveMissionScript, _newPrefab, _deletePrefab, _addMember, _removeMember;

    public ObservableCollection<Mission> Missions => _profile.Missions;
    public ObservableCollection<MissionPrefab> MissionPrefabs => _profile.Prefabs;
    public IReadOnlyList<MissionKind> MissionKinds { get; } =
        GmSpawn.Actions.Select(a => new MissionKind(a.Key, a.Label)).Prepend(new MissionKind(ItemAction.Key, ItemAction.Label)).ToList();

    /// <summary>Asks the mission map to show a world spot and this many world units around it.</summary>
    public event Action<Vec3, double>? MissionGoRequested;

    /// <summary>Frames the selected phase: its area and all its pins.</summary>
    public void FrameSelectedPhase()
    {
        if (_selectedPhase?.Center is not { } c) return;
        var reach = _selectedPhase.Spawns.Select(s => MissionEngine.Distance(s.At, c)).DefaultIfEmpty(0).Max();
        MissionGoRequested?.Invoke(c, Math.Max(_selectedPhase.RadiusM * 100, reach));
    }

    private void InitMissions()
    {
        if (_profile.Prefabs.Count == 0)
        {
            var sample = new MissionPrefab { Name = "Rager patrol" };
            sample.Members.Add(new PrefabMember { Kind = "vehicle", Type = "BPC_Rager" });
            sample.Members.Add(new PrefabMember { Kind = "npcs", Type = "BP_Drifter_Lvl_5", Copies = 6, OffsetM = 5 });
            _profile.Prefabs.Add(sample);
        }
        _selectedPrefab = _profile.Prefabs[0];
        SelectedMission = _profile.Missions.FirstOrDefault();
    }

    // ---- selection

    private Mission? _selectedMission;
    public Mission? SelectedMission
    {
        get => _selectedMission;
        set
        {
            if (!Set(ref _selectedMission, value)) return;
            Raise(nameof(HasMission));
            SelectedPhase = value?.Phases.FirstOrDefault();
            RefreshTally();
            MapChanged?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool HasMission => _selectedMission != null;

    private MissionPhase? _selectedPhase;
    public MissionPhase? SelectedPhase
    {
        get => _selectedPhase;
        set
        {
            var old = _selectedPhase;
            if (!Set(ref _selectedPhase, value)) return;
            if (old != null) old.PropertyChanged -= MissionObjectChanged;
            if (value != null) value.PropertyChanged += MissionObjectChanged;
            Raise(nameof(HasPhase));
            Raise(nameof(PhasePosition));
            SelectedSpawn = null;
            FrameSelectedPhase();
            MapChanged?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool HasPhase => _selectedPhase != null;

    public string PhasePosition => _selectedMission == null || _selectedPhase == null
        ? ""
        : $"Phase {_selectedMission.Phases.IndexOf(_selectedPhase) + 1} of {_selectedMission.Phases.Count}";

    private MissionSpawn? _selectedSpawn;
    public MissionSpawn? SelectedSpawn
    {
        get => _selectedSpawn;
        set
        {
            var old = _selectedSpawn;
            if (!Set(ref _selectedSpawn, value)) return;
            if (old != null) old.PropertyChanged -= MissionObjectChanged;
            if (value != null) value.PropertyChanged += MissionObjectChanged;
            Raise(nameof(EditSpawn));
            Raise(nameof(EditSpawnHeading));
            Raise(nameof(EditSpawnTypes));
            MapChanged?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>The spawn form edits the selected pin, or with none selected, the settings the next new pin gets.</summary>
    public MissionSpawn EditSpawn => _selectedSpawn ?? _spawnDefaults;
    public string EditSpawnHeading => _selectedSpawn != null ? "Selected spawn" : "Next spawn";

    private void MissionObjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MissionSpawn.Kind)) Raise(nameof(EditSpawnTypes));
        MapChanged?.Invoke();
    }

    public IReadOnlyList<string> EditSpawnTypes => TypesFor(EditSpawn.Kind);

    private IReadOnlyList<string> TypesFor(string kind) => kind == ItemAction.Key
        ? _itemCatalog.Items
        : GmActions.FirstOrDefault(a => a.Action.Key == kind)?.Types ?? Array.Empty<string>();

    private MissionPrefab? _selectedPrefab;
    public MissionPrefab? SelectedPrefab { get => _selectedPrefab; set => Set(ref _selectedPrefab, value); }

    // ---- map tools

    private MissionTool _tool = MissionTool.Select;
    public MissionTool Tool
    {
        get => _tool;
        set
        {
            if (!Set(ref _tool, value)) return;
            Raise(nameof(ToolSelect)); Raise(nameof(ToolCentre)); Raise(nameof(ToolSpawn)); Raise(nameof(ToolPrefab)); Raise(nameof(ToolMove));
            Raise(nameof(MissionHint));
        }
    }
    public bool ToolSelect { get => _tool == MissionTool.Select; set { if (value) Tool = MissionTool.Select; } }
    public bool ToolCentre { get => _tool == MissionTool.Centre; set { if (value) Tool = MissionTool.Centre; } }
    public bool ToolSpawn { get => _tool == MissionTool.Spawn; set { if (value) Tool = MissionTool.Spawn; } }
    public bool ToolPrefab { get => _tool == MissionTool.Prefab; set { if (value) Tool = MissionTool.Prefab; } }
    public bool ToolMove { get => _tool == MissionTool.Move; set { if (value) Tool = MissionTool.Move; } }

    public string MissionHint => _tool switch
    {
        MissionTool.Centre => "Click the map to set this phase's centre.",
        MissionTool.Spawn => "Click the map to place spawns, using the settings under Next spawn. Esc to finish.",
        MissionTool.Prefab => "Click the map to place the selected prefab scene. Esc to finish.",
        MissionTool.Move => "Click the map to move the selected spawn there.",
        _ => "Click a numbered centre or a pin to select it. Drag to pan, scroll to zoom.",
    };

    /// <summary>A click on the mission map, by tool. <paramref name="pixels"/> measures on-screen distance between two world spots.</summary>
    public void MissionMapClicked(Vec3 world, Func<Vec3, Vec3, double> pixels)
    {
        if (_selectedMission == null) { Status = "Create a mission first (New, top left)."; return; }
        switch (_tool)
        {
            case MissionTool.Select:
                SelectAt(world, pixels);
                return;
            case MissionTool.Centre:
                EnsurePhase().Center = world with { Z = GroundGuess(world).Z };
                Tool = MissionTool.Select;
                break;
            case MissionTool.Spawn:
                var pin = EditSpawn.CloneAt(world with { Z = GroundGuess(world).Z });
                pin.Group = "";
                EnsurePhase().Spawns.Add(pin);
                Status = $"Added {pin.Summary} {pin.RuleText}.";
                break;
            case MissionTool.Prefab:
                if (_selectedPrefab == null || _selectedPrefab.Members.Count == 0) { Status = "Pick a prefab with at least one member (Plan → Prefab scenes)."; return; }
                var pins = MissionEngine.Expand(_selectedPrefab, world with { Z = GroundGuess(world).Z }, _spawnDefaults.TriggerRadiusM, _spawnDefaults.HoldRadiusM);
                var phase = EnsurePhase();
                foreach (var p in pins) phase.Spawns.Add(p);
                Status = $"Added '{_selectedPrefab.Name}': {pins.Count} pin(s).";
                break;
            case MissionTool.Move:
                if (_selectedSpawn == null) { Status = "Select a spawn to move first."; Tool = MissionTool.Select; return; }
                _selectedSpawn.At = world with { Z = GroundGuess(world).Z };
                Tool = MissionTool.Select;
                break;
        }
        Save();
        MapChanged?.Invoke();
    }

    private void SelectAt(Vec3 world, Func<Vec3, Vec3, double> pixels)
    {
        // Pins of the phase in view first: they sit on top of its centre.
        var pin = _selectedPhase?.Spawns.Select(s => (s, d: pixels(s.At, world))).Where(t => t.d <= PinHitPixels).OrderBy(t => t.d).Select(t => t.s).FirstOrDefault();
        if (pin != null) { SelectedSpawn = pin; return; }
        var phase = _selectedMission!.Phases.Where(p => p.Center != null).Select(p => (p, d: pixels(p.Center!.Value, world)))
            .Where(t => t.d <= PinHitPixels).OrderBy(t => t.d).Select(t => t.p).FirstOrDefault();
        if (phase != null) SelectedPhase = phase;
        else SelectedSpawn = null;
    }

    private MissionPhase EnsurePhase()
    {
        if (_selectedPhase != null) return _selectedPhase;
        var phase = new MissionPhase { Name = $"Phase {_selectedMission!.Phases.Count + 1}" };
        _selectedMission.Phases.Add(phase);
        SelectedPhase = phase;
        return phase;
    }

    /// <summary>The best height known for a spot right now, from the same sources as GM spawn.</summary>
    private (double Z, string Source, bool Learned) GroundGuess(Vec3 p)
    {
        if (Settings.GmUseSetHeight) return (Settings.GmSetHeight, "set height", false);
        if (LearnedGround(p, LearnedWinsWithin) is { } lg) return (lg.Z, lg.Source, true);
        if (NearbyGround(p) is { } g) return (g.Z, g.Source, false);
        if (EstimatedGround(p) is { } e) return (e.Z, e.Source, false);
        return (Settings.MapTeleportZ, "no map markers: the Map Z setting", false);
    }

    // ---- plan commands

    public ICommand NewMissionCommand => _newMission ??= new RelayCommand(() =>
    {
        var mission = new Mission { Name = $"Mission {Missions.Count + 1}" };
        mission.Phases.Add(new MissionPhase { Name = "Phase 1" });
        Missions.Add(mission);
        SelectedMission = mission;
        Tool = MissionTool.Centre;
        Save();
    });

    public ICommand DeleteMissionCommand => _deleteMission ??= new RelayCommand(() =>
    {
        if (_selectedMission is not { } m || !_confirm($"Delete mission '{m.Name}'?")) return;
        if (_runPhase != null && m.Phases.Contains(_runPhase)) StopRun("");
        Missions.Remove(m);
        SelectedMission = Missions.FirstOrDefault();
        Save();
    }, () => HasMission);

    public ICommand AddPhaseCommand => _addPhase ??= new RelayCommand(() =>
    {
        var phase = new MissionPhase { Name = $"Phase {_selectedMission!.Phases.Count + 1}", RadiusM = _selectedPhase?.RadiusM ?? 150 };
        _selectedMission.Phases.Add(phase);
        SelectedPhase = phase;
        Tool = MissionTool.Centre;
        Save();
    }, () => HasMission);

    public ICommand RemovePhaseCommand => _removePhase ??= new RelayCommand(() =>
    {
        var phase = _selectedPhase!;
        if (phase.Spawns.Count > 0 && !_confirm($"Delete '{phase.Name}' and its {phase.Spawns.Count} spawn(s)?")) return;
        if (phase == _runPhase) StopRun("");
        var phases = _selectedMission!.Phases;
        var i = phases.IndexOf(phase);
        phases.Remove(phase);
        SelectedPhase = phases.Count == 0 ? null : phases[Math.Min(i, phases.Count - 1)];
        Save();
    }, () => HasPhase);

    private void MovePhase(int by)
    {
        var phases = _selectedMission!.Phases;
        var i = phases.IndexOf(_selectedPhase!);
        var to = i + by;
        if (to < 0 || to >= phases.Count) return;
        phases.Move(i, to);
        Raise(nameof(PhasePosition));
        MapChanged?.Invoke();
        Save();
    }

    public ICommand PhaseUpCommand => _phaseUp ??= new RelayCommand(() => MovePhase(-1), () => HasPhase);
    public ICommand PhaseDownCommand => _phaseDown ??= new RelayCommand(() => MovePhase(1), () => HasPhase);

    private void StepPhase(int by)
    {
        var phases = _selectedMission!.Phases;
        var to = phases.IndexOf(_selectedPhase!) + by;
        if (to >= 0 && to < phases.Count) SelectedPhase = phases[to];
    }

    public ICommand PrevPhaseCommand => _prevPhase ??= new RelayCommand(() => StepPhase(-1), () => _selectedMission != null && _selectedPhase != null && _selectedMission.Phases.IndexOf(_selectedPhase) > 0);
    public ICommand NextPhaseCommand => _nextPhase ??= new RelayCommand(() => StepPhase(1), () => _selectedMission != null && _selectedPhase != null && _selectedMission.Phases.IndexOf(_selectedPhase) < _selectedMission.Phases.Count - 1);

    public ICommand RemoveSpawnCommand => _removeSpawn ??= new RelayCommand(() =>
    {
        _selectedPhase!.Spawns.Remove(_selectedSpawn!);
        SelectedSpawn = null;
        RefreshTally();
        MapChanged?.Invoke();
        Save();
    }, () => _selectedSpawn != null && _selectedPhase != null);

    public ICommand NewPrefabCommand => _newPrefab ??= new RelayCommand(() =>
    {
        var prefab = new MissionPrefab { Name = $"Prefab {MissionPrefabs.Count + 1}" };
        prefab.Members.Add(new PrefabMember());
        MissionPrefabs.Add(prefab);
        SelectedPrefab = prefab;
        Save();
    });

    public ICommand DeletePrefabCommand => _deletePrefab ??= new RelayCommand(() =>
    {
        if (_selectedPrefab is not { } p || !_confirm($"Delete prefab '{p.Name}'? Pins already placed stay.")) return;
        MissionPrefabs.Remove(p);
        SelectedPrefab = MissionPrefabs.FirstOrDefault();
        Save();
    }, () => _selectedPrefab != null);

    public ICommand AddPrefabMemberCommand => _addMember ??= new RelayCommand(() => { _selectedPrefab?.Members.Add(new PrefabMember()); Save(); }, () => _selectedPrefab != null);
    public ICommand RemovePrefabMemberCommand => _removeMember ??= new RelayCommand(o => { if (o is PrefabMember m) { _selectedPrefab?.Members.Remove(m); Save(); } });

    // ---- running a phase

    private bool _missionRunning;
    public bool MissionRunning { get => _missionRunning; private set { if (Set(ref _missionRunning, value)) { Raise(nameof(MissionNotRunning)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool MissionNotRunning => !_missionRunning;

    private string _runStatus = "Not running.";
    public string RunStatus { get => _runStatus; private set => Set(ref _runStatus, value); }

    public string MissionPoll
    {
        get => Settings.MissionPollSeconds.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) { Settings.MissionPollSeconds = Math.Max(MinMissionPollSeconds, s); Save(); }
            Raise();
        }
    }

    public bool MissionIgnoreMe { get => Settings.MissionIgnoreMe; set { Settings.MissionIgnoreMe = value; Raise(); Save(); } }

    public ICommand RunPhaseCommand => _runPhaseCmd ??= new AsyncCommand(RunPhaseAsync, () => HasPhase);

    private async Task RunPhaseAsync()
    {
        var phase = _selectedPhase!;
        if (phase.Spawns.Count == 0 && phase.Announce.Length == 0) { Status = "This phase has nothing to run yet: place some spawns."; return; }
        if (!Settings.DryRun && phase.Spawns.Any(s => s.State == MissionSpawnState.Pending && MissionEngine.NeedsPlayers(s))
            && !_confirm($"'{phase.Name}' has spawns that depend on where players are.\n\n"
                + $"While it runs, SACC reads #ListPlayers about every {Settings.MissionPollSeconds} s. Each read brings SCUM to the front, opens chat and types, "
                + "and replaces anything half-typed in chat.\n\nStart it?"))
            return;

        _runPhase = phase;
        _missionMisses = 0;
        MissionRunning = true;
        RunStatus = $"Running '{phase.Name}'.";

        if (phase.Announce.Length > 0 && _catalog.Find("announce") is { } def)
        {
            var line = TemplateRenderer.Render(def, new Dictionary<string, string?> { ["message"] = phase.Announce }).Text;
            if (Settings.DryRun) Record(line, "dry run");
            else if (await GuardedAsync(ct => _game.SendAsync(Settings, new[] { line }, _ownWindow(), ct))) Record(line, "sent");
            else { StopRun($"Stopped: {Status}"); return; }
        }

        _missionTimer ??= CreateMissionTimer();
        _missionTimer.Start();
    }

    private DispatcherTimer CreateMissionTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await MissionTickAsync();
        return timer;
    }

    public ICommand StopRunCommand => _stopRun ??= new RelayCommand(() => StopRun("Stopped."), () => MissionRunning);

    private void StopRun(string status)
    {
        _missionTimer?.Stop();
        if (_runPhase != null) foreach (var s in _runPhase.Spawns) s.Waiting = "";
        _runPhase = null;
        MissionRunning = false;
        RunStatus = status.Length > 0 ? status : "Not running.";
    }

    /// <summary>One pin per tick, re-checking positions in between: a long climb can leave them stale.</summary>
    private async Task MissionTickAsync()
    {
        if (!_missionRunning || _missionBusy || IsBusy || _runPhase is not { } phase) return;
        var pending = phase.Spawns.Where(s => s.State == MissionSpawnState.Pending).ToList();
        if (pending.Count == 0)
        {
            StopRun($"'{phase.Name}': every spawn has fired. Check the tally, then run Cleanup when it's over.");
            return;
        }
        if (!Settings.DryRun && !GameOk) { RunStatus = $"Waiting for the game ({GameStatusText})."; return; }

        _missionBusy = true;
        try
        {
            if (pending.Any(MissionEngine.NeedsPlayers) && !await RefreshMissionPositionsAsync()) return;
            var players = MissionPlayers();

            MissionSpawn? next = null;
            // Ground-checked pins first: their climbs measure the ground, which then places vehicles and items near them.
            foreach (var s in pending.OrderBy(s => ActionFor(s.Kind)?.GroundChecked == true ? 0 : 1))
            {
                var d = MissionEngine.Decide(s, players);
                s.Waiting = d switch
                {
                    MissionEngine.Decision.Held => $"Held: a player is within {s.HoldRadiusM:0} m",
                    MissionEngine.Decision.WaitForPlayer => players == null ? "Waiting for player positions" : $"Waiting for a player within {s.TriggerRadiusM:0} m",
                    _ => "Spawning…",
                };
                if (d == MissionEngine.Decision.Fire && next == null) next = s;
            }
            RunStatus = next != null
                ? $"Running '{phase.Name}': spawning {next.Summary}."
                : $"Running '{phase.Name}': {pending.Count} spawn(s) waiting on their rules.";
            if (next != null && !await FireSpawnAsync(next))
                StopRun($"Stopped: {Status}");
        }
        finally { _missionBusy = false; }
    }

    /// <summary>Reads the player list when it's older than the poll time. False = the run has been stopped.</summary>
    private async Task<bool> RefreshMissionPositionsAsync()
    {
        if (Settings.DryRun) return true;
        var at = _profile.PlayersListedAt;
        if (at is { } t && (DateTime.Now - t).TotalSeconds < Settings.MissionPollSeconds) return true;
        switch (await ReadPlayersQuietlyAsync())
        {
            case PlayerRead.Failed:
                StopRun($"Stopped: {Status}");
                return false;
            case PlayerRead.NoAnswer:
                if (++_missionMisses < MaxTrackMisses) return true;
                StopRun($"Stopped: the game didn't answer {MaxTrackMisses} player reads in a row.");
                return false;
            default:
                _missionMisses = 0;
                return true;
        }
    }

    /// <summary>Where the online players are, or null if that isn't known recently enough to act on.</summary>
    private List<Vec3>? MissionPlayers()
    {
        if (_profile.PlayersListedAt is not { } at) return null;
        // In dry run the last pasted list stands, so rules can be tried without the game.
        if (!Settings.DryRun && (DateTime.Now - at).TotalSeconds > 3.0 * Settings.MissionPollSeconds) return null;
        var me = Settings.MissionIgnoreMe ? Settings.MySteamId.Trim() : "";
        return Players.Where(p => p.LastSeen >= at && p.Location != null && (me.Length == 0 || p.SteamId != me))
            .Select(p => p.Location!.Value).ToList();
    }

    private static GmSpawn.Action? ActionFor(string kind) =>
        kind == ItemAction.Key ? ItemAction : GmSpawn.Actions.FirstOrDefault(a => a.Key == kind);

    /// <summary>The chat lines for a pin at a height; null (with Status set) if they can't be built.</summary>
    private List<string>? MissionLines(MissionSpawn s, GmSpawn.Action action, Vec3 at)
    {
        if (action.Key == "kit")
        {
            var kit = Kits.FirstOrDefault(k => k.Label == s.Type) ?? Kits.FirstOrDefault(k => k.Name == s.Type);
            if (kit == null) { Status = $"No kit called '{s.Type}'."; return null; }
            if (KitLines(kit, out _) is not { } kitLines) return null;
            var placed = GmSpawn.KitAt(kitLines, at, out _);
            return Enumerable.Repeat(placed, s.Count).SelectMany(l => l).ToList();
        }
        var def = _catalog.Find(action.CatalogId);
        if (def == null) { Status = $"The command catalog has no '{action.CatalogId}' entry."; return null; }
        var r = GmSpawn.Render(def, action, s.Type, s.Count, at);
        if (!r.Ok) { Status = $"{s.Summary}: {string.Join("  ", r.Errors)}"; return null; }
        return r.Text.Split('\n').ToList();
    }

    /// <summary>Sends one pin and records what really happened. False = the send itself failed or was stopped.</summary>
    private async Task<bool> FireSpawnAsync(MissionSpawn s)
    {
        s.Waiting = "";
        var action = ActionFor(s.Kind);
        if (action == null) { s.State = MissionSpawnState.Refused; Status = $"Unknown spawn kind '{s.Kind}'."; return true; }

        var guess = GroundGuess(s.At);
        s.At = s.At with { Z = guess.Z };
        var at = s.At with { Z = guess.Z + Settings.GmHeightOffset };
        var lines = MissionLines(s, action, at);
        if (lines == null) { s.Refused = s.Count; s.State = MissionSpawnState.Refused; Finish(s); return true; }
        var items = MissionEngine.ItemCodes(lines).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (Settings.DryRun)
        {
            foreach (var l in lines) Record(l, "dry run");
            MissionEngine.RecordSend(s, lines.Count, default);
            s.ItemCodes = items;
        }
        else if (action.GroundChecked && Settings.GmFindGround && _catalog.Find(action.CatalogId) is { } def)
        {
            var spawned = await GmClimbAsync(action, s.Type, s.Summary, def, s.Count, at, guess.Learned);
            if (spawned < 0) return false;
            MissionEngine.RecordClimb(s, spawned);
        }
        else
        {
            var log = AppPaths.ScumClientLog;
            long seen = File.Exists(log) ? new FileInfo(log).Length : -1;
            if (!await GuardedAsync(ct => _game.SendAsync(Settings, lines, _ownWindow(), ct))) return false;
            foreach (var l in lines) Record(l, "sent");
            // Replies reach the log almost at once.
            await Task.Delay(1500);
            var replies = seen < 0 ? default : MissionEngine.ReadReplies(await ReadSharedAsync(log, Math.Min(seen, new FileInfo(log).Length)));
            MissionEngine.RecordSend(s, lines.Count, replies);
            if (s.State != MissionSpawnState.Refused) s.ItemCodes = items;
        }
        Finish(s);
        return true;
    }

    private void Finish(MissionSpawn s)
    {
        RefreshTally();
        MapChanged?.Invoke();
        Save();
    }

    private IReadOnlyList<MissionEngine.TallyRow> _missionTally = Array.Empty<MissionEngine.TallyRow>();
    /// <summary>What really spawned in this mission, by type.</summary>
    public IReadOnlyList<MissionEngine.TallyRow> MissionTally { get => _missionTally; private set => Set(ref _missionTally, value); }

    private void RefreshTally() =>
        MissionTally = _selectedMission == null ? Array.Empty<MissionEngine.TallyRow>() : MissionEngine.Tally(_selectedMission.Phases.SelectMany(p => p.Spawns));

    public ICommand ResetRunCommand => _resetRun ??= new RelayCommand(() =>
    {
        var phase = _selectedPhase!;
        if (!_confirm($"Forget what '{phase.Name}' has spawned, so it can run again? What's in the world stays, and cleanup will no longer know about it.")) return;
        foreach (var s in phase.Spawns)
        {
            s.State = MissionSpawnState.Pending;
            s.Spawned = s.Refused = 0;
            s.ItemCodes.Clear();
        }
        RefreshTally();
        MapChanged?.Invoke();
        Save();
    }, () => HasPhase && !MissionRunning);

    public ICommand CleanupPhaseCommand => _cleanupPhase ??= new AsyncCommand(CleanupPhaseAsync, () => HasPhase && !MissionRunning);

    private async Task CleanupPhaseAsync()
    {
        var phase = _selectedPhase!;
        var plan = MissionEngine.Cleanup(phase, _catalog.Find("teleport"), _catalog.Find("destroy-items-radius"));
        if (plan.Error != null) { Status = plan.Error; return; }
        var missed = plan.NotCovered.Count > 0 ? $"Not covered, no command known: {string.Join(", ", plan.NotCovered)}." : "";
        if (plan.Lines.Count == 0) { Status = $"Nothing to clean up for '{phase.Name}'. {missed}"; return; }

        var near = phase.Center is { } c && _profile.PlayersListedAt is { } at
            ? Players.Where(p => p.LastSeen >= at && p.Location is { } l && MissionEngine.Distance(l, c) <= phase.RadiusM * 100).Select(p => p.Nickname.Length > 0 ? p.Nickname : p.SteamId).ToList()
            : new List<string>();
        var warning = "This teleports you to the phase centre, and destroys every item of these types in the radius, including players' own."
            + (near.Count > 0 ? $"\n\nIn the area at the last player read: {string.Join(", ", near)}." : "")
            + (missed.Length > 0 ? $"\n\n{missed}" : "");
        if (!_confirm($"Run cleanup for '{phase.Name}'?\n\n{string.Join("\n", plan.Lines)}\n\n{warning}")) return;

        if (Settings.DryRun)
        {
            foreach (var l in plan.Lines) Record(l, "dry run");
            Status = $"Dry run: cleanup not sent. {missed}";
            return;
        }
        var log = AppPaths.ScumClientLog;
        long seen = File.Exists(log) ? new FileInfo(log).Length : -1;
        if (!await GuardedAsync(ct => _game.SendAsync(Settings, plan.Lines, _ownWindow(), ct))) return;
        foreach (var l in plan.Lines) Record(l, "sent");
        await Task.Delay(1500);
        var destroyed = seen < 0 ? 0 : MissionEngine.ItemsDestroyed(await ReadSharedAsync(log, Math.Min(seen, new FileInfo(log).Length)));
        Status = $"Cleanup sent: the game reports {destroyed} item(s) destroyed. {missed}";
    }

    // ---- export

    private WhalleyBotExport.Export? MissionExport()
    {
        if (_selectedMission is not { } m) return null;
        return MissionEngine.ToWhalleyBot(m, s =>
        {
            if (ActionFor(s.Kind) is not { } action) return null;
            return MissionLines(s, action, s.At with { Z = GroundGuess(s.At).Z + Settings.GmHeightOffset });
        }, _catalog.Find("announce"), _catalog.Find("teleport"), _catalog.Find("destroy-items-radius"), Settings.MaxSpawnPerCommand);
    }

    public ICommand CopyMissionScriptCommand => _copyMissionScript ??= new RelayCommand(() =>
    {
        if (MissionExport() is not { } e) return;
        GameLink.SetClipboardText(e.Text);
        Status = $"Copied '{_selectedMission!.Name}' for WhalleyBot, one block per phase. " + string.Join(" ", e.Notes);
    }, () => HasMission);

    public ICommand SaveMissionScriptCommand => _saveMissionScript ??= new RelayCommand(() =>
    {
        if (MissionExport() is not { } e) return;
        var name = new string(_selectedMission!.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        var dialog = new SaveFileDialog { Title = "Save WhalleyBot script", FileName = $"{name}-whalleybot.txt", DefaultExt = ".txt", Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, e.Text);
            Status = $"Saved {dialog.FileName}. " + string.Join(" ", e.Notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = $"Couldn't save: {ex.Message}"; }
    }, () => HasMission);
}
