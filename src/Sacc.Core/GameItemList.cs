using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sacc.Core;

/// <summary>
/// The game's own item list. #ListItems prints one chat line per item and never touches the
/// clipboard, but SCUM's client log (SCUM.log) records every line between
/// "START OF 'Item' LIST" and "END OF 'Item' LIST", so the list is read from there.
/// </summary>
public static partial class GameItemList
{
    /// <summary>A block shorter than this is a filtered search ("#ListItems mag"), not the whole list.</summary>
    public const int MinFullList = 500;

    [GeneratedRegex(@"LogSCUM: START OF '(\w+)' LIST\s*$")]
    private static partial Regex Start();

    [GeneratedRegex(@"LogSCUM: END OF '(\w+)' LIST\s*$")]
    private static partial Regex End();

    // Entries are indented ("LogSCUM:     Weapon_AK47"); other LogSCUM lines have a single space.
    [GeneratedRegex(@"LogSCUM: {2,}(\S+)\s*$")]
    private static partial Regex Entry();

    /// <summary>Every complete list of <paramref name="kind"/> in the log text, oldest first.</summary>
    public static List<List<string>> ParseLists(string logText, string kind = "Item")
    {
        var lists = new List<List<string>>();
        List<string>? current = null;
        foreach (var line in logText.Split('\n'))
        {
            if (Start().Match(line) is { Success: true } s)
            {
                current = s.Groups[1].Value.Equals(kind, StringComparison.OrdinalIgnoreCase) ? new List<string>() : null;
                continue;
            }
            if (current == null) continue;
            if (End().IsMatch(line)) { lists.Add(current); current = null; continue; }
            if (Entry().Match(line) is { Success: true } e) current.Add(e.Groups[1].Value);
        }
        return lists;
    }

    /// <summary>The newest complete, unfiltered item list in the log, or null.</summary>
    public static List<string>? LastFullList(string logText) =>
        ParseLists(logText).LastOrDefault(l => l.Count >= MinFullList);

    public sealed class FileModel
    {
        public string Source { get; set; } = "#ListItems (SCUM client log)";
        public DateTime ReadAt { get; set; }
        public List<string> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(IEnumerable<string> items, DateTime readAt) =>
        JsonSerializer.Serialize(new FileModel { ReadAt = readAt, Items = items.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList() }, Json);

    public static FileModel? Load(string path)
    {
        try { return JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}
