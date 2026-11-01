using System.Text.RegularExpressions;

namespace Sacc.Core;

/// <summary>
/// The list of known spawn codes, used for type-ahead in the Spawner. Built from the game's
/// own #ListItems list (its spelling wins), the item table (bare codes and "#spawnvehicle X"
/// entries) and codes the user already has in kits and favorites. Item codes the game list
/// doesn't contain are kept but marked <see cref="IsUnlisted"/> and sorted last.
/// </summary>
public sealed class ItemCatalog
{
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Vehicles { get; }
    /// <summary>Item codes not in the game's list (DLC, renamed or retired items); empty without a game list.</summary>
    public IReadOnlySet<string> Unlisted { get; }

    public ItemCatalog(IEnumerable<string> tableValues, IEnumerable<string>? extraItems = null, IEnumerable<string>? extraVehicles = null,
                       IEnumerable<string>? gameItems = null)
    {
        // Keyed case-insensitively; the first spelling added wins, so game spellings go in first.
        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var vehicles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in gameItems ?? Enumerable.Empty<string>()) if (IsCode(c)) items.TryAdd(c, c);
        var listed = new HashSet<string>(items.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var value in tableValues)
        {
            if (value.StartsWith("#spawnvehicle ", StringComparison.OrdinalIgnoreCase))
                vehicles.Add(value["#spawnvehicle ".Length..].Trim());
            else if (IsCode(value))
                items.TryAdd(value, value);
        }
        foreach (var c in extraItems ?? Enumerable.Empty<string>()) if (IsCode(c)) items.TryAdd(c, c);
        foreach (var c in extraVehicles ?? Enumerable.Empty<string>()) if (IsCode(c)) vehicles.Add(c);

        var unlisted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (listed.Count > 0) foreach (var c in items.Keys) if (!listed.Contains(c)) unlisted.Add(c);
        Unlisted = unlisted;
        Items = items.Values.OrderBy(c => unlisted.Contains(c)).ThenBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        Vehicles = vehicles.ToList();
    }

    public bool IsUnlisted(string code) => Unlisted.Contains(code);

    private static bool IsCode(string? c) => !string.IsNullOrWhiteSpace(c) && !c.StartsWith('#') && !c.Any(char.IsWhiteSpace);

    /// <summary>
    /// Matches every word of the query anywhere in the code ("m16 mag" finds Magazine_M16).
    /// Exact match first, then prefix matches, then the rest; codes the game doesn't list after
    /// those it does; alphabetical within each.
    /// </summary>
    public static List<string> Filter(IReadOnlyList<string> codes, string? query, int max = 200, IReadOnlySet<string>? unlisted = null)
    {
        var words = (query ?? "").Split(new[] { ' ', '_', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return codes.Take(max).ToList();
        var q = query!.Trim();
        return codes
            .Where(c => words.All(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Equals(q, StringComparison.OrdinalIgnoreCase) ? 0 : c.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(c => unlisted?.Contains(c) == true)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    public static int Count(IReadOnlyList<string> codes, string? query)
    {
        var words = (query ?? "").Split(new[] { ' ', '_', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? codes.Count : codes.Count(c => words.All(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Compile the user's magazine pattern; an invalid pattern matches nothing rather than crashing a spawn.</summary>
    public static Func<string, bool> MagazineMatcher(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return _ => false;
        try
        {
            var re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            return code => re.IsMatch(code);
        }
        catch (ArgumentException) { return _ => false; }
    }
}
