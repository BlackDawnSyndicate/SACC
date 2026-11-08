using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>The Mission tab's rules, away from the UI: when a pin fires, what really spawned, what cleanup can reach, and the export.</summary>
public static partial class MissionEngine
{
    public enum Decision { Fire, WaitForPlayer, Held }

    public static bool NeedsPlayers(MissionSpawn s) => s.TriggerRadiusM > 0 || s.HoldRadiusM > 0;

    /// <summary>What a pending pin should do given where the players are. Null players = positions unknown, so a pin that depends on them waits.</summary>
    public static Decision Decide(MissionSpawn s, IReadOnlyCollection<Vec3>? players)
    {
        if (!NeedsPlayers(s)) return Decision.Fire;
        if (players == null) return Decision.WaitForPlayer;
        // Hold wins: "spawn when someone is near, but not on top of them" is a ring between the two radii.
        if (s.HoldRadiusM > 0 && players.Any(p => Distance(p, s.At) <= s.HoldRadiusM * 100)) return Decision.Held;
        if (s.TriggerRadiusM > 0 && !players.Any(p => Distance(p, s.At) <= s.TriggerRadiusM * 100)) return Decision.WaitForPlayer;
        return Decision.Fire;
    }

    /// <summary>Across the ground only: a player on a roof or down a bunker is still "at" the spot.</summary>
    public static double Distance(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---- what the game said (SCUM.log echoes its chat replies)

    public readonly record struct Replies(int Spawned, int Refused, int NotAllowed);

    [GeneratedRegex(@"for message Spawned \S")]
    private static partial Regex SpawnedReply();

    [GeneratedRegex(@"for message '[^']*' is not allowed to be spawned")]
    private static partial Regex NotAllowedReply();

    [GeneratedRegex(@"for message (\d+) items destroyed")]
    private static partial Regex DestroyedReply();

    [GeneratedRegex(@"^#SpawnItem\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpawnItemCode();

    public static Replies ReadReplies(string? log) => string.IsNullOrEmpty(log)
        ? default
        : new Replies(SpawnedReply().Count(log), GmSpawn.CountRefusals(log), NotAllowedReply().Count(log));

    /// <summary>The "N items destroyed" total in a chunk of SCUM.log.</summary>
    public static int ItemsDestroyed(string? log) => string.IsNullOrEmpty(log)
        ? 0
        : DestroyedReply().Matches(log).Sum(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));

    /// <summary>Item codes in spawn lines, for cleanup.</summary>
    public static IEnumerable<string> ItemCodes(IEnumerable<string> lines) =>
        lines.Select(l => SpawnItemCode().Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value);

    /// <summary>Records a ground-checked climb, whose refusals are counted, so what wasn't refused did spawn.</summary>
    public static void RecordClimb(MissionSpawn s, int spawned)
    {
        s.Spawned = spawned;
        s.Refused = s.Count - spawned;
        s.State = spawned >= s.Count ? MissionSpawnState.Spawned : spawned > 0 ? MissionSpawnState.Partial : MissionSpawnState.Refused;
    }

    /// <summary>
    /// Records a plain send from the game's replies. "Spawned X." lines confirm items; other kinds get no reply on
    /// success, so unless something was refused they stay "sent", and don't count as spawned.
    /// </summary>
    public static void RecordSend(MissionSpawn s, int linesSent, Replies r)
    {
        var refused = r.Refused + r.NotAllowed;
        if (refused == 0 && r.Spawned == 0)
        {
            s.State = MissionSpawnState.Sent;
            return;
        }
        if (r.Spawned >= linesSent && refused == 0)
        {
            s.Spawned = s.Count;
            s.State = MissionSpawnState.Spawned;
            return;
        }
        if (r.Spawned == 0)
        {
            s.Refused = refused;
            s.State = refused >= Math.Max(linesSent, 1) ? MissionSpawnState.Refused : MissionSpawnState.Sent;
            return;
        }
        s.Spawned = Math.Min(s.Count, r.Spawned);
        s.Refused = refused;
        s.State = MissionSpawnState.Partial;
    }

    // ---- the tally

    public sealed record TallyRow(string Type, int Asked, int Spawned, int Refused, int Unconfirmed);

    /// <summary>What actually happened, by type, for spawns that have fired. Refused ones never count as spawned.</summary>
    public static List<TallyRow> Tally(IEnumerable<MissionSpawn> spawns) => spawns
        .Where(s => s.State != MissionSpawnState.Pending)
        .GroupBy(s => s.Type.Length > 0 ? s.Type : s.Kind, StringComparer.OrdinalIgnoreCase)
        .Select(g => new TallyRow(g.Key, g.Sum(s => s.Count), g.Sum(s => s.Spawned), g.Sum(s => s.Refused),
            g.Where(s => s.State == MissionSpawnState.Sent).Sum(s => s.Count - s.Refused)))
        .OrderBy(r => r.Type, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // ---- cleanup

    public sealed record CleanupPlan(List<string> Lines, List<string> NotCovered, string? Error);

    /// <summary>
    /// A phase's cleanup. #DestroyAllItemsWithinRadius works around the admin and takes one item type, so the plan
    /// teleports you to the phase centre, then destroys each item type this phase spawned, then runs your own lines.
    /// Creatures and vehicles have no such command yet; they're listed as not covered.
    /// </summary>
    public static CleanupPlan Cleanup(MissionPhase phase, CommandDef? teleport, CommandDef? destroy)
    {
        var lines = new List<string>();
        var fired = phase.Spawns.Where(s => s.State != MissionSpawnState.Pending).ToList();
        var codes = fired.SelectMany(s => s.ItemCodes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var extras = phase.ExtraCleanup.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var notCovered = fired.Where(s => s.ItemCodes.Count == 0 && s.Spawned + (s.State == MissionSpawnState.Sent ? 1 : 0) > 0)
            .Select(s => s.Type.Length > 0 ? s.Type : s.Kind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (codes.Count > 0)
        {
            if (phase.Center is not { } c) return new CleanupPlan(lines, notCovered, "This phase has no centre to clean up around.");
            if (teleport == null || destroy == null) return new CleanupPlan(lines, notCovered, "The command catalog is missing 'teleport' or 'destroy-items-radius'.");
            lines.Add(TemplateRenderer.Render(teleport, new Dictionary<string, string?>
            {
                ["x"] = c.X.ToString("0.###", CultureInfo.InvariantCulture),
                ["y"] = c.Y.ToString("0.###", CultureInfo.InvariantCulture),
                ["z"] = c.Z.ToString("0.###", CultureInfo.InvariantCulture),
            }).Text);
            var radius = ((int)Math.Ceiling(Math.Max(1, phase.RadiusM))).ToString(CultureInfo.InvariantCulture);
            lines.AddRange(codes.Select(code => TemplateRenderer.Render(destroy, new Dictionary<string, string?> { ["item"] = code, ["radius"] = radius }).Text));
        }
        lines.AddRange(extras);
        return new CleanupPlan(lines, notCovered, null);
    }

    // ---- prefabs

    /// <summary>The pins a prefab adds at <paramref name="anchor"/>: each member's copies in an even ring, starting north.</summary>
    public static List<MissionSpawn> Expand(MissionPrefab prefab, Vec3 anchor, double triggerRadiusM = 0, double holdRadiusM = 0)
    {
        var pins = new List<MissionSpawn>();
        foreach (var m in prefab.Members)
            for (var i = 0; i < m.Copies; i++)
            {
                var angle = 2 * Math.PI * i / m.Copies;
                var r = m.OffsetM * 100;
                pins.Add(new MissionSpawn
                {
                    Kind = m.Kind, Type = m.Type, Count = m.Count, Group = prefab.Name,
                    At = anchor with { X = Math.Round(anchor.X + r * Math.Sin(angle), 2), Y = Math.Round(anchor.Y + r * Math.Cos(angle), 2) },
                    TriggerRadiusM = triggerRadiusM, HoldRadiusM = holdRadiusM,
                });
            }
        return pins;
    }

    // ---- WhalleyBot

    /// <summary>
    /// One block per phase, to paste as one WhalleyBot task each. A task runs its lines top to bottom when started,
    /// so radius rules can't come along; they're kept as comments and reported in the notes.
    /// </summary>
    public static WhalleyBotExport.Export ToWhalleyBot(Mission mission, Func<MissionSpawn, IReadOnlyList<string>?> spawnLines,
                                                       CommandDef? announce, CommandDef? teleport, CommandDef? destroy, int maxSpawnPerLine)
    {
        var text = new StringBuilder();
        var notes = new List<string>();
        void Note(string n) { if (!notes.Contains(n)) notes.Add(n); }

        for (var i = 0; i < mission.Phases.Count; i++)
        {
            var phase = mission.Phases[i];
            if (i > 0) text.Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"// ==== {mission.Name}: phase {i + 1} of {mission.Phases.Count}: {phase.Name} ====\n");
            if (phase.Center is { } c)
                text.Append(CultureInfo.InvariantCulture, $"// Centre {WhalleyBotExport.Coords(c)}, radius {phase.RadiusM:0} m\n");
            if (phase.Announce.Length > 0 && announce != null)
                text.Append(TemplateRenderer.Render(announce, new Dictionary<string, string?> { ["message"] = phase.Announce }).Text).Append('\n');

            foreach (var s in phase.Spawns)
            {
                if (NeedsPlayers(s))
                {
                    text.Append("// Only ").Append(s.RuleText).Append(" in SACC; this task sends it at once:\n");
                    Note("Radius rules became comments: a WhalleyBot task sends every line when it starts. To stage them, split those lines into their own task.");
                }
                var lines = spawnLines(s);
                if (lines == null) { text.Append("// Skipped ").Append(s.Summary).Append(": it couldn't be rendered\n"); Note($"{s.Summary} was skipped."); continue; }
                foreach (var line in lines)
                    foreach (var split in CommandLine.SplitSpawn(line, maxSpawnPerLine))
                        text.Append(split).Append('\n');
            }

            // Planned cleanup: everything this phase would spawn, not just what a run has recorded.
            var items = ItemCodes(phase.Spawns.SelectMany(s => spawnLines(s) ?? Array.Empty<string>())).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var extras = phase.ExtraCleanup.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (items.Count > 0 || extras.Count > 0)
            {
                text.Append("// ---- Cleanup: make this its own task, and run it when the phase is over ----\n");
                if (items.Count > 0 && phase.Center is { } cc && teleport != null && destroy != null)
                {
                    var radius = ((int)Math.Ceiling(Math.Max(1, phase.RadiusM))).ToString(CultureInfo.InvariantCulture);
                    text.Append(TemplateRenderer.Render(teleport, new Dictionary<string, string?>
                    {
                        ["x"] = cc.X.ToString("0.###", CultureInfo.InvariantCulture),
                        ["y"] = cc.Y.ToString("0.###", CultureInfo.InvariantCulture),
                        ["z"] = cc.Z.ToString("0.###", CultureInfo.InvariantCulture),
                    }).Text).Append('\n');
                    foreach (var code in items)
                        text.Append(TemplateRenderer.Render(destroy, new Dictionary<string, string?> { ["item"] = code, ["radius"] = radius }).Text).Append('\n');
                    Note("Cleanup teleports whoever runs it to the phase centre, because #DestroyAllItemsWithinRadius works around them. It destroys every item of those types in the radius, players' included.");
                }
                foreach (var e in extras) text.Append(e).Append('\n');
            }
        }

        Note("Heights are fixed in a task, with no climb: a pin the game refuses stays refused. Pins on measured ground are the safest; spawn them once in SACC first.");
        if (mission.Phases.Count == 0) Note("The mission has no phases.");
        return new WhalleyBotExport.Export(text.ToString().TrimEnd('\n'), notes);
    }
}
