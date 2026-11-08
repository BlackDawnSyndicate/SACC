using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Sacc.App.Game;
using Sacc.App.Infrastructure;
using Sacc.Core;

namespace Sacc.App.ViewModels;

/// <summary>A choice in the map's track filter; a null SteamId means everyone.</summary>
public sealed record TrackChoice(string? SteamId, string Label);

/// <summary>
/// Players → Tracking: read #ListPlayers every so often and keep each player's positions in tracks.csv,
/// for export and for the map's Tracks layer. Off at every start, because each read types into the game.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MinTrackSeconds = 10;
    private const int MaxTrackMisses = 3;

    private readonly PlayerTracks _tracks = new();
    private DispatcherTimer? _trackTimer;
    private bool _tracking;
    private int _trackMisses;
    private ICommand? _exportTrack, _exportAllTracks, _clearTracks, _showTrack;

    public ObservableCollection<TrackChoice> TrackChoices { get; } = new() { new TrackChoice(null, "Everyone") };

    private void InitTracking()
    {
        _mapTrackChoice = TrackChoices[0];
        try
        {
            if (File.Exists(AppPaths.Tracks)) _tracks.Load(File.ReadLines(AppPaths.Tracks));
        }
        catch (IOException ex) { FileLog.Write("WARN", $"Tracks unreadable: {ex.Message}"); }
        RefreshTrackChoices();
    }

    public bool Tracking
    {
        get => _tracking;
        set
        {
            if (value == _tracking) return;
            if (!value) { StopTracking("Tracking stopped."); return; }
            if (!_confirm($"Track players every {Settings.TrackIntervalSeconds} s?\n\n"
                + "Each read brings SCUM to the front, opens chat and types #ListPlayers true. It takes a moment, it can cut across "
                + "what you're doing in game, and it replaces anything half-typed in chat. The clipboard's text is put back, but an image on it is lost.\n\n"
                + "It stops when you untick it or close SACC."))
            {
                Raise();
                return;
            }
            _tracking = true;
            _trackMisses = 0;
            Raise();
            _trackTimer ??= CreateTrackTimer();
            _trackTimer.Interval = TimeSpan.FromSeconds(Settings.TrackIntervalSeconds);
            _trackTimer.Start();
            _ = TrackTickAsync();
        }
    }

    public string TrackInterval
    {
        get => Settings.TrackIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s))
            {
                Settings.TrackIntervalSeconds = Math.Max(MinTrackSeconds, s);
                if (_trackTimer != null) _trackTimer.Interval = TimeSpan.FromSeconds(Settings.TrackIntervalSeconds);
                InvalidateMapTracks();
                Save();
            }
            Raise();
        }
    }

    public string TrackingLabel => _tracks.PointCount == 0
        ? "Nothing tracked yet."
        : $"{_tracks.PointCount:N0} point(s) for {_tracks.PlayerCount} player(s).";

    private DispatcherTimer CreateTrackTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += async (_, _) => await TrackTickAsync();
        return timer;
    }

    private void StopTracking(string status)
    {
        _tracking = false;
        _trackTimer?.Stop();
        Raise(nameof(Tracking));
        Status = status;
    }

    private async Task TrackTickAsync()
    {
        // Something else is sending: the next tick reads the list instead.
        if (!_tracking || IsBusy) return;
        if (Settings.DryRun) { Status = "Tracking is on, but Dry run is too: nothing is sent, and only lists you paste are recorded."; return; }
        if (!GameOk) { Status = $"Tracking: waiting for the game ({GameStatusText})."; return; }

        _trackTimer?.Stop();
        try
        {
            switch (await ReadPlayersQuietlyAsync())
            {
                case PlayerRead.Failed:
                    StopTracking($"{Status} Tracking stopped.");
                    return;
                case PlayerRead.NoAnswer:
                    if (++_trackMisses >= MaxTrackMisses) StopTracking($"Tracking stopped: the game didn't answer {MaxTrackMisses} reads in a row.");
                    else Status = "Tracking: the game didn't answer this read.";
                    return;
                default:
                    _trackMisses = 0;
                    return;
            }
        }
        finally
        {
            if (_tracking) _trackTimer?.Start();
        }
    }

    private enum PlayerRead { Answered, NoAnswer, Failed }

    /// <summary>An unattended #ListPlayers: back to whichever window you were in afterwards, and no History row.</summary>
    private async Task<PlayerRead> ReadPlayersQuietlyAsync()
    {
        var def = _catalog.Find("list-players-copy");
        if (def == null) { Status = "The command catalog has no 'list-players-copy' entry."; return PlayerRead.Failed; }
        var returnTo = GameLink.ForegroundWindow;
        string? output = null;
        var ok = await GuardedAsync(async ct => output = await _game.SendAndCaptureAsync(Settings, def.Template, returnTo, ct, alwaysReturn: true));
        if (!ok) return PlayerRead.Failed;
        if (output == null) return PlayerRead.NoAnswer;
        MergePlayers(output);
        return PlayerRead.Answered;
    }

    /// <summary>Called with every player list read while tracking is on, automatic or by hand.</summary>
    private void RecordTracks(IEnumerable<Sacc.Core.Model.PlayerRecord> players)
    {
        if (!_tracking) return;
        var added = _tracks.Add(players, DateTimeOffset.Now);
        if (added.Count == 0) return;
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var lines = added.Select(PlayerTracks.ToCsv);
            File.AppendAllLines(AppPaths.Tracks, File.Exists(AppPaths.Tracks) ? lines : lines.Prepend(PlayerTracks.CsvHeader));
        }
        catch (IOException ex) { Status = $"Couldn't save tracks: {ex.Message}"; }
        RefreshTrackChoices();
    }

    private void RefreshTrackChoices()
    {
        foreach (var (id, name) in _tracks.Players)
        {
            var label = string.IsNullOrWhiteSpace(name) ? id : name;
            var i = TrackChoices.ToList().FindIndex(c => c.SteamId == id);
            if (i < 0) TrackChoices.Add(new TrackChoice(id, label));
        }
        Raise(nameof(TrackingLabel));
        Raise(nameof(MapTracksLabel));
        InvalidateMapTracks();
    }

    private static string TrackFileName(string who)
    {
        var name = new string(who.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return $"track-{name}-{DateTime.Now:yyyy-MM-dd}.csv";
    }

    private void ExportTrack(string? steamId, string who)
    {
        var lines = _tracks.ExportLines(steamId);
        if (lines.Count <= 1) { Status = $"No track for {who} yet."; return; }
        var dialog = new SaveFileDialog { Title = $"Export track: {who}", FileName = TrackFileName(who), DefaultExt = ".csv", Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllLines(dialog.FileName, lines);
            Status = $"Exported {lines.Count - 1} point(s) to {dialog.FileName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = $"Couldn't export: {ex.Message}"; }
    }

    public ICommand ExportTrackCommand => _exportTrack ??= new RelayCommand(() =>
    {
        if (SelectedPlayer is not { } p) { Status = "Select a player first."; return; }
        ExportTrack(p.SteamId, string.IsNullOrWhiteSpace(p.Nickname) ? p.SteamId : p.Nickname);
    });

    public ICommand ExportAllTracksCommand => _exportAllTracks ??= new RelayCommand(() => ExportTrack(null, "everyone"));

    public ICommand ClearTracksCommand => _clearTracks ??= new RelayCommand(() =>
    {
        if (_tracks.PointCount == 0 || !_confirm($"Delete all {_tracks.PointCount:N0} tracked point(s)?")) return;
        try { File.Delete(AppPaths.Tracks); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = $"Couldn't delete tracks: {ex.Message}"; return; }
        _tracks.Clear();
        while (TrackChoices.Count > 1) TrackChoices.RemoveAt(TrackChoices.Count - 1);
        MapTrackChoice = TrackChoices[0];
        RefreshTrackChoices();
    });

    /// <summary>Puts the selected player's track on the map and centres on where they were last.</summary>
    public ICommand ShowTrackCommand => _showTrack ??= new RelayCommand(() =>
    {
        if (SelectedPlayer is not { } p) { Status = "Select a player first."; return; }
        if (TrackChoices.FirstOrDefault(c => c.SteamId == p.SteamId) is not { } choice) { Status = $"No track for {p.Display} yet."; return; }
        MapTrackChoice = choice;
        MapShowTracks = true;
        var last = _tracks.Segments(p.SteamId, DateTimeOffset.MinValue, TimeSpan.MaxValue).LastOrDefault()?.LastOrDefault();
        if (last != null) MapGoRequested?.Invoke(last.Location, 4);
        Status = $"{p.Display}'s track is on the Map tab (last {MapTrackHours} h).";
    });

    // ---- the map's Tracks layer

    private bool _mapShowTracks;
    public bool MapShowTracks { get => _mapShowTracks; set { if (Set(ref _mapShowTracks, value)) MapChanged?.Invoke(); } }

    private TrackChoice _mapTrackChoice = null!;
    public TrackChoice MapTrackChoice
    {
        get => _mapTrackChoice;
        set { if (value != null && Set(ref _mapTrackChoice, value)) InvalidateMapTracks(); }
    }

    private string _mapTrackHours = "24";
    /// <summary>How far back the Tracks layer reaches; 0 or empty = all of it.</summary>
    public string MapTrackHours
    {
        get => _mapTrackHours;
        set { if (Set(ref _mapTrackHours, value ?? "")) InvalidateMapTracks(); }
    }

    public string MapTracksLabel => $"Player tracks ({_tracks.PlayerCount})";

    private List<List<TrackPoint>>? _mapTracks;

    /// <summary>The runs the map draws; rebuilt when tracks or the filter change.</summary>
    public IReadOnlyList<IReadOnlyList<TrackPoint>> MapTracks => _mapTracks ??= BuildMapTracks();

    private List<List<TrackPoint>> BuildMapTracks()
    {
        var since = double.TryParse(_mapTrackHours, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0
            ? DateTimeOffset.Now.AddHours(-h)
            : DateTimeOffset.MinValue;
        // Over a heartbeat with no point, and over a few missed reads, means they left.
        var gap = TimeSpan.FromSeconds(Math.Max(PlayerTracks.Heartbeat.TotalSeconds + 60, 3.0 * Settings.TrackIntervalSeconds));
        return _tracks.Segments(_mapTrackChoice?.SteamId, since, gap);
    }

    private void InvalidateMapTracks()
    {
        _mapTracks = null;
        MapChanged?.Invoke();
    }
}
