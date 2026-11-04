using System.Globalization;
using System.Text;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>Where a player was at one moment.</summary>
public sealed record TrackPoint(DateTimeOffset At, string SteamId, string Name, Vec3 Location);

/// <summary>
/// Player positions over time, built from successive #ListPlayers outputs. Kept as CSV, one row per point,
/// so the file can be appended to while tracking and opened as it is.
/// </summary>
public sealed class PlayerTracks
{
    public const string CsvHeader = "time,steam_id,name,x,y,z";
    /// <summary>A player who moved less than this (1 m) since their last point isn't recorded again…</summary>
    public const double MinMove = 100;
    /// <summary>…until this long has passed, so standing still can be told from having left.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, List<TrackPoint>> _byPlayer = new();

    public int PointCount => _byPlayer.Values.Sum(l => l.Count);
    public int PlayerCount => _byPlayer.Count;

    /// <summary>Every tracked player with their most recent name, by name.</summary>
    public IReadOnlyList<(string SteamId, string Name)> Players =>
        _byPlayer.Select(kv => (kv.Key, kv.Value[^1].Name)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public void Clear() => _byPlayer.Clear();

    /// <summary>Adds the points from one player list; returns those worth keeping (new, moved, or due a heartbeat).</summary>
    public List<TrackPoint> Add(IEnumerable<PlayerRecord> players, DateTimeOffset now)
    {
        var added = new List<TrackPoint>();
        foreach (var p in players)
        {
            if (p.Location is not { } at) continue;
            if (_byPlayer.TryGetValue(p.SteamId, out var list))
            {
                var last = list[^1];
                var dx = at.X - last.Location.X; var dy = at.Y - last.Location.Y; var dz = at.Z - last.Location.Z;
                if (dx * dx + dy * dy + dz * dz < MinMove * MinMove && now - last.At < Heartbeat) continue;
            }
            var point = new TrackPoint(now, p.SteamId, p.Nickname, at);
            Append(point);
            added.Add(point);
        }
        return added;
    }

    /// <summary>Loads rows from a tracks file; the header and damaged lines are skipped.</summary>
    public void Load(IEnumerable<string> lines)
    {
        foreach (var line in lines)
            if (ParseCsv(line) is { } p) Append(p);
    }

    private void Append(TrackPoint p)
    {
        if (!_byPlayer.TryGetValue(p.SteamId, out var list)) _byPlayer[p.SteamId] = list = new();
        list.Add(p);
    }

    /// <summary>
    /// A player's points since a time (everyone's when <paramref name="steamId"/> is null), cut into one run per
    /// session: a gap over <paramref name="maxGap"/> means they left, so no line is drawn across it.
    /// </summary>
    public List<List<TrackPoint>> Segments(string? steamId, DateTimeOffset since, TimeSpan maxGap)
    {
        var result = new List<List<TrackPoint>>();
        foreach (var (id, points) in _byPlayer)
        {
            if (steamId != null && id != steamId) continue;
            List<TrackPoint>? run = null;
            foreach (var p in points)
            {
                if (p.At < since) continue;
                if (run == null || p.At - run[^1].At > maxGap) result.Add(run = new());
                run.Add(p);
            }
        }
        return result;
    }

    /// <summary>The CSV for one player, or everyone in time order when <paramref name="steamId"/> is null.</summary>
    public List<string> ExportLines(string? steamId)
    {
        var points = steamId != null
            ? _byPlayer.GetValueOrDefault(steamId) ?? new List<TrackPoint>()
            : _byPlayer.Values.SelectMany(l => l).OrderBy(p => p.At).ToList();
        var lines = new List<string>(points.Count + 1) { CsvHeader };
        lines.AddRange(points.Select(ToCsv));
        return lines;
    }

    public static string ToCsv(TrackPoint p) => string.Join(',',
        p.At.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        p.SteamId,
        CsvName(p.Name),
        Num(p.Location.X), Num(p.Location.Y), Num(p.Location.Z));

    public static TrackPoint? ParseCsv(string line)
    {
        var f = SplitCsv(line);
        if (f.Count != 6) return null;
        if (!DateTimeOffset.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return null;
        if (!TryNum(f[3], out var x) || !TryNum(f[4], out var y) || !TryNum(f[5], out var z)) return null;
        var name = f[2];
        if (name.Length > 1 && name[0] == '\'' && IsFormulaStart(name[1])) name = name[1..];
        return new TrackPoint(at, f[1], name, new Vec3(x, y, z));
    }

    /// <summary>Player names are anyone's text: keep them on one line, and stop a spreadsheet running one as a formula.</summary>
    private static string CsvName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (clean.Length > 0 && IsFormulaStart(clean[0])) clean = "'" + clean;
        return clean.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + clean.Replace("\"", "\"\"") + "\"" : clean;
    }

    private static bool IsFormulaStart(char c) => c is '=' or '+' or '-' or '@';

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields;
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static bool TryNum(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
