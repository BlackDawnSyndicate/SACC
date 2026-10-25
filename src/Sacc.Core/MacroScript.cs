using System.Globalization;
using System.Text.RegularExpressions;

namespace Sacc.Core;

/// <summary>
/// A macro is plain text: one admin command per line, "wait &lt;ms&gt;" to pause,
/// "//" for comments, and {variables} filled in at run time.
/// </summary>
public static partial class MacroScript
{
    public const int MaxWaitMs = 60_000;

    public abstract record Step(int Line);
    public sealed record SendStep(int Line, string Text) : Step(Line);
    public sealed record WaitStep(int Line, int Milliseconds) : Step(Line);

    public sealed record Result(IReadOnlyList<Step> Steps, IReadOnlyList<string> Errors)
    {
        public bool Ok => Errors.Count == 0;
    }

    [GeneratedRegex(@"^wait\s+(\d+)\s*(ms)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Wait();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Variable();

    public static IReadOnlyList<string> Variables(string script) =>
        Variable().Matches(script).Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static Result Parse(string script, IReadOnlyDictionary<string, string>? vars = null)
    {
        var steps = new List<Step>();
        var errors = new List<string>();
        var lines = script.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            int n = i + 1;
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            var wait = Wait().Match(line);
            if (wait.Success)
            {
                if (!int.TryParse(wait.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms > MaxWaitMs)
                    errors.Add($"Line {n}: wait must be between 0 and {MaxWaitMs} ms.");
                else
                    steps.Add(new WaitStep(n, ms));
                continue;
            }

            var expanded = Variable().Replace(line, m =>
            {
                var key = m.Groups[1].Value;
                if (vars != null && vars.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
                errors.Add($"Line {n}: variable {{{key}}} has no value.");
                return m.Value;
            });

            var check = CommandLine.Check(expanded);
            if (!check.Ok) errors.Add($"Line {n}: {check.Error}");
            else steps.Add(new SendStep(n, check.Text));
        }

        if (steps.Count == 0 && errors.Count == 0)
            errors.Add("Macro has no commands.");
        return new Result(steps, errors);
    }
}
