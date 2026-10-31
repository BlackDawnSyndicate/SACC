namespace Sacc.Core;

/// <summary>Validation for a single chat line about to be sent to the game.</summary>
public static class CommandLine
{
    public const int SoftLengthLimit = 240;

    public sealed record Result(bool Ok, string Text, string? Error, string? Warning);

    public static Result Check(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
            return new(false, text, "Command is empty.", null);
        if (text[0] != '#')
            return new(false, text, "Admin commands start with '#'. SACC will not send plain chat.", null);
        // A newline would press Enter mid-line and send a second, unintended message.
        if (text.Any(char.IsControl))
            return new(false, text, "Command contains a line break or control character.", null);
        var warning = text.Length > SoftLengthLimit
            ? $"Line is {text.Length} characters; the game's chat box may truncate it."
            : null;
        return new(true, text, null, warning);
    }

    private static readonly System.Text.RegularExpressions.Regex SpawnAmount = new(
        @"^(#SpawnItem\s+\S+\s+)(\d+)(\s.*)?$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits "#SpawnItem code 23 [...]" into lines of at most <paramref name="maxPerLine"/>
    /// (10, 10, 3), the same chunking kits use. Anything else comes back unchanged.
    /// </summary>
    public static IEnumerable<string> SplitSpawn(string line, int maxPerLine)
    {
        var m = SpawnAmount.Match(line);
        var chunk = Math.Max(1, maxPerLine);
        if (!m.Success || !int.TryParse(m.Groups[2].Value, out var amount) || amount <= chunk)
        {
            yield return line;
            yield break;
        }
        for (var left = amount; left > 0; left -= chunk)
            yield return m.Groups[1].Value + Math.Min(chunk, left) + m.Groups[3].Value;
    }
}
