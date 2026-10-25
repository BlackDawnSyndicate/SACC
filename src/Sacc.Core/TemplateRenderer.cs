using System.Globalization;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>Turns a catalog entry plus user-entered values into a chat line.</summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    public sealed record Result(string Text, IReadOnlyList<string> Errors)
    {
        public bool Ok => Errors.Count == 0;
    }

    public static Result Render(CommandDef def, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new List<string>();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in def.Params)
        {
            values.TryGetValue(p.Name, out var v);
            v = string.IsNullOrWhiteSpace(v) ? p.Default : v.Trim();
            if (string.IsNullOrWhiteSpace(v))
            {
                if (!p.Optional) errors.Add($"'{p.Name}' is required.");
                resolved[p.Name] = "";
                continue;
            }
            var error = ValidateValue(p, v, out var normalized);
            if (error != null) errors.Add(error);
            resolved[p.Name] = normalized;
        }

        var text = Token().Replace(def.Template, m =>
            resolved.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        text = MultiSpace().Replace(text, " ").Trim();

        var leftover = Token().Match(text);
        if (leftover.Success)
            errors.Add($"Template placeholder {leftover.Value} has no matching parameter.");

        return new Result(text, errors);
    }

    private static string? ValidateValue(ParamDef p, string v, out string normalized)
    {
        normalized = v;
        if (v.Any(char.IsControl))
            return $"'{p.Name}' contains a line break or control character.";

        switch (p.Kind)
        {
            case ParamKind.Integer:
                if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return $"'{p.Name}' must be a whole number.";
                normalized = l.ToString(CultureInfo.InvariantCulture);
                return null;

            case ParamKind.Number:
                // Accept a comma decimal from locales that use one; the game wants a dot.
                if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return $"'{p.Name}' must be a number.";
                normalized = d.ToString("0.###", CultureInfo.InvariantCulture);
                return null;

            case ParamKind.Bool:
                if (bool.TryParse(v, out var b)) { normalized = b ? "true" : "false"; return null; }
                return $"'{p.Name}' must be true or false.";

            case ParamKind.Choice:
                var match = p.Choices.FirstOrDefault(c => string.Equals(c, v, StringComparison.OrdinalIgnoreCase));
                if (match == null) return $"'{p.Name}' must be one of: {string.Join(", ", p.Choices)}.";
                normalized = match;
                return null;

            case ParamKind.Player:
            case ParamKind.Text:
                if (v.Contains(' '))
                    return $"'{p.Name}' cannot contain spaces (use a SteamID64 for players with spaces in their name).";
                return null;

            default: // Message: free text, spaces allowed.
                return null;
        }
    }
}
