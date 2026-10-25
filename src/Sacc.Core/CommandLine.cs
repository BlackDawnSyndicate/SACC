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
}
