using System.Globalization;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>
/// Text for pasting into WhalleyBot's panel: scheduled tasks, packs and task lines.
/// SACC never talks to WhalleyBot; it only writes in WhalleyBot's own task syntax.
/// </summary>
public static partial class WhalleyBotExport
{
    public sealed record Export(string Text, IReadOnlyList<string> Notes);

    [GeneratedRegex(@"^wait\s+(\d+)\s*(ms)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Wait();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Variable();

    /// <summary>
    /// A SACC macro as a WhalleyBot task: "wait N" becomes #taskpause (seconds, rounded up),
    /// {player} becomes &lt;argument&gt; (the SteamID a pack passes with "#ftask name [SteamID]"),
    /// {me} becomes your SteamID64, other {x} become &lt;x&gt; task variables, comments stay
    /// and large spawns split like every SACC send.
    /// </summary>
    public static Export MacroToTask(string script, string? mySteamId, int maxSpawnPerLine)
    {
        var lines = new List<string>();
        var notes = new List<string>();
        void Note(string n) { if (!notes.Contains(n)) notes.Add(n); }

        foreach (var raw in script.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // Tasks take // comments too, so they carry over.
            if (line.StartsWith("//")) { lines.Add(line); continue; }

            var wait = Wait().Match(line);
            if (wait.Success)
            {
                var ms = long.Parse(wait.Groups[1].Value, CultureInfo.InvariantCulture);
                if (ms == 0) continue;
                lines.Add($"#taskpause {(ms + 999) / 1000}");
                Note("Waits became #taskpause. WhalleyBot runs a task's in-game lines as one batch, so a pause may not space out game commands.");
                continue;
            }

            line = Variable().Replace(line, m =>
            {
                var key = m.Groups[1].Value;
                if (key.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    Note("{player} became <argument>: run the task from a pack with \"#ftask taskname [SteamID]\" so it gets the buyer's SteamID.");
                    return "<argument>";
                }
                if (key.Equals("me", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(mySteamId)) return mySteamId.Trim();
                    Note("{me} was left as is: set your SteamID64 in Settings.");
                    return m.Value;
                }
                Note($"{{{key}}} became <{key}>: define it in the task with #var_{key} first.");
                return $"<{key}>";
            });

            if (!line.StartsWith('#'))
                Note($"Kept a line that isn't a # command: \"{line}\". Check it before pasting.");
            lines.AddRange(CommandLine.SplitSpawn(line, maxSpawnPerLine));
        }

        if (!lines.Any(l => !l.StartsWith("//"))) Note("The macro has no commands.");
        return new Export(string.Join("\n", lines), notes);
    }

    /// <summary>"X Y Z" as WhalleyBot's task commands write coordinates.</summary>
    public static string Coords(Vec3 p) =>
        string.Create(CultureInfo.InvariantCulture, $"{p.X:0.###} {p.Y:0.###} {p.Z:0.###}");

    /// <summary>
    /// A random-choice task variable, e.g. "#var_coords {X Y Z;X Y Z}"; WhalleyBot picks one
    /// entry each time the task uses &lt;coords&gt;.
    /// </summary>
    public static string CoordList(string name, IEnumerable<Vec3> points) =>
        $"#var_{name} {{{string.Join(";", points.Select(Coords))}}}";

    /// <summary>Task lines for a map spot, by kind: xyz (panel location fields), location, map, razor, npc.</summary>
    public static string? SpotLine(string kind, Vec3 p) => kind switch
    {
        "xyz" => Coords(p),
        "location" => $"#location \"{Coords(p)}\"",
        "map" => $"#map {Coords(p)} \"Event\"",
        "razor" => $"#spawnrazor \"{Coords(p)}\"",
        "npc" => $"#spawnarmedNPC BP_Drifter_LvL_3 1 Location \"{Coords(p)}\"",
        _ => null,
    };
}
