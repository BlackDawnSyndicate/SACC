using System.Text;

namespace Sacc.Core;

/// <summary>
/// Turns a raw spawn code into a readable label when the game gives no name of its own, e.g.
/// <c>1H_Improvised_metal_knife</c> → "Improvised Metal Knife", <c>Magazine_M16</c> → "Magazine M16".
/// A browsing aid only — it never changes the code SACC sends, and real names from a metadata file win over it.
/// </summary>
public static class ItemName
{
    // Hand prefixes expanded rather than dropped, so a melee weapon still reads naturally.
    private static readonly Dictionary<string, string> Expand = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1h"] = "One-handed", ["2h"] = "Two-handed", ["bp"] = "",
    };

    // Tokens kept all-caps instead of title-cased.
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "m16", "m9", "m1", "ak47", "ak74", "ak", "smg", "rpg", "mp5", "fmj", "hp", "nato", "led", "gps",
        "usb", "dlc", "tv", "pc", "uk", "us", "atv", "suv", "9mm", "556", "762", "308", "45",
    };

    public static string Prettify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var parts = code.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var raw in parts)
        {
            var token = raw;
            if (Expand.TryGetValue(token, out var expanded))
            {
                if (expanded.Length == 0) continue; // dropped (e.g. the BP_ blueprint prefix)
                token = expanded;
            }
            else if (Acronyms.Contains(token))
            {
                token = token.ToUpperInvariant();
            }
            else if (token.All(char.IsDigit))
            {
                // Drop pure index suffixes like the "01"/"02" in Hiking_Backpack_01_02; keep a lone number.
                if (token.Length <= 2 && sb.Length > 0) continue;
            }
            else
            {
                token = char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
            }

            if (sb.Length > 0) sb.Append(' ');
            sb.Append(token);
        }
        return sb.Length > 0 ? sb.ToString() : code;
    }
}
