using System.Text.Json.Serialization;

namespace Sacc.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ParamKind { Text, Integer, Number, Player, Bool, Choice, Message }

/// <summary>How sure we are that a catalog entry matches the live game's syntax.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Confidence
{
    /// <summary>Syntax agrees across public server-host documentation.</summary>
    Documented,
    /// <summary>Public sources disagree on the syntax; verify in-game.</summary>
    Conflicting,
    /// <summary>Recalled from community use but not found in a public source.</summary>
    Unverified,
}

public sealed class ParamDef
{
    public string Name { get; set; } = "";
    public ParamKind Kind { get; set; } = ParamKind.Text;
    public bool Optional { get; set; }
    public string? Default { get; set; }
    public List<string> Choices { get; set; } = new();
    public string? Hint { get; set; }
}

public sealed class CommandDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    /// <summary>Chat line with {param} placeholders, e.g. "#SpawnItem {item} {amount}".</summary>
    public string Template { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Notes { get; set; }
    public Confidence Confidence { get; set; } = Confidence.Unverified;
    public bool Dangerous { get; set; }
    /// <summary>When true the game is expected to put the command's output on the clipboard.</summary>
    public bool CapturesClipboard { get; set; }
    public List<ParamDef> Params { get; set; } = new();
}

public sealed class CommandCatalog
{
    public int Version { get; set; } = 1;
    public List<CommandDef> Commands { get; set; } = new();

    public CommandDef? Find(string id) =>
        Commands.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}
