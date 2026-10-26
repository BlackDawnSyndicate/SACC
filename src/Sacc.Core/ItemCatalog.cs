using System.Text.RegularExpressions;

namespace Sacc.Core;

/// <summary>
/// The list of known spawn codes, used for type-ahead in the Spawner. Built from the
/// item table (bare codes and "#spawnvehicle X" entries) plus codes the user already has
/// in kits and favorites, so newly patched-in items appear as soon as they're used once.
/// </summary>
public sealed class ItemCatalog
{
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Vehicles { get; }

    public ItemCatalog(IEnumerable<string> tableValues, IEnumerable<string>? extraItems = null, IEnumerable<string>? extraVehicles = null)
    {
        var items = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var vehicles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in tableValues)
        {
            if (value.StartsWith("#spawnvehicle ", StringComparison.OrdinalIgnoreCase))
                vehicles.Add(value["#spawnvehicle ".Length..].Trim());
            else if (!value.StartsWith('#'))
                items.Add(value);
        }
        foreach (var c in extraItems ?? Enumerable.Empty<string>()) if (IsCode(c)) items.Add(c);
        foreach (var c in extraVehicles ?? Enumerable.Empty<string>()) if (IsCode(c)) vehicles.Add(c);
        Items = items.ToList();
        Vehicles = vehicles.ToList();
    }

    private static bool IsCode(string? c) => !string.IsNullOrWhiteSpace(c) && !c.StartsWith('#') && !c.Any(char.IsWhiteSpace);

    /// <summary>
    /// Matches every word of the query anywhere in the code ("m16 mag" finds Magazine_M16).
    /// Exact match first, then prefix matches, then the rest; alphabetical within each.
    /// </summary>
    public static List<string> Filter(IReadOnlyList<string> codes, string? query, int max = 200)
    {
        var words = (query ?? "").Split(new[] { ' ', '_', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return codes.Take(max).ToList();
        var q = query!.Trim();
        return codes
            .Where(c => words.All(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Equals(q, StringComparison.OrdinalIgnoreCase) ? 0 : c.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
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
